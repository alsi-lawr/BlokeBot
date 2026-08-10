using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CommunityProgression;

public partial class CommunityProgressionPage
{
    private IReadOnlyList<CommunitySeasonView> _seasons = [];
    private readonly Dictionary<Guid, ScheduleEditDraft> _scheduleEdits = [];
    private readonly HashSet<string> _closedStages = [];
    private SeasonDraft _season = SeasonDraft.New();
    private RewardDraft _reward = new();
    private DefinitionDraft _definition = new();
    private bool _enabled;
    private bool _failed;
    private string _feedback = string.Empty;

    private IReadOnlyList<CommunityEventRuleDescriptor> _availableEventRules =>
        CommunityEventRuleCatalog.AvailableFor(_definition.Kind, _definition.Scope);

    private string _publicUrl => $"/community/{Uri.EscapeDataString(HostLogin)}";

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (HostId == 0)
        {
            return;
        }
        var features = await _features.Load(HostId).ExecuteAsync(CancellationToken.None);
        var configured = features.Match(
            option => option.Match(value => value, () => HostFeatureFlags.None),
            _ => throw new UnreachableException()
        );
        _enabled = configured.Contains(HostFeatureFlags.CommunityProgression);
        _seasons = _enabled
            ? await _progression.GetModeratorSeasonsAsync(HostId, CancellationToken.None)
            : [];
    }

    private async Task CreateSeasonAsync()
    {
        if (!TryUtc(_season.StartsAtUtc, out var start) || !TryUtc(_season.EndsAtUtc, out var end))
        {
            Fail("Season start and end must be valid UTC dates and times.");
            return;
        }
        await MutateAsync(() =>
            _progression.CreateSeasonAsync(
                HostId,
                new(
                    Guid.NewGuid(),
                    _season.Name,
                    _season.Description,
                    _season.ModeratorNotes,
                    _season.Visibility,
                    start,
                    end,
                    Actor()
                ),
                CancellationToken.None
            )
        );
        if (!_failed)
        {
            _season = SeasonDraft.New();
        }
    }

    private async Task AddRewardAsync(CommunitySeasonView season)
    {
        await MutateAsync(() =>
            _progression.AddRewardAsync(
                HostId,
                new(
                    Guid.NewGuid(),
                    season.Id,
                    _reward.Key,
                    _reward.Kind,
                    _reward.Name,
                    _reward.PresentationToken,
                    Actor()
                ),
                CancellationToken.None
            )
        );
        if (!_failed)
        {
            _reward = new();
        }
    }

    private async Task AddDefinitionAsync(CommunitySeasonView season)
    {
        if (!long.TryParse(_definition.Target, out var target) || target <= 0)
        {
            Fail("Target must be a positive whole number.");
            return;
        }
        var points = PointAmount
            .ParseNonNegativeAbsolute(_definition.PointsReward)
            .Match<PointAmount?>(value => value, _ => null);
        if (
            points is null
            || !TimeOnly.TryParseExact(_definition.ResetLocalTime, "HH:mm", out var localTime)
        )
        {
            Fail("Points must be non-negative and local reset time must use HH:mm.");
            return;
        }
        var rewardKeys = _definition.RewardKeys.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var rewardIds = season
            .Rewards.Where(value =>
                rewardKeys.Contains(value.Key, StringComparer.OrdinalIgnoreCase)
            )
            .Select(value => value.Id)
            .ToArray();
        if (rewardIds.Length != rewardKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            Fail("Every reward key must identify a reward in this season.");
            return;
        }
        await MutateAsync(() =>
            _progression.AddDefinitionAsync(
                HostId,
                new(
                    Guid.NewGuid(),
                    season.Id,
                    _definition.Key,
                    _definition.Name,
                    _definition.Description,
                    _definition.Kind,
                    _definition.Scope,
                    _definition.Completion,
                    _definition.EventRule,
                    _definition.Increment,
                    _definition.FilterToken,
                    target,
                    points.Value,
                    new(
                        _definition.ResetCadence,
                        localTime,
                        _definition.ResetCadence == CommunityResetCadence.Weekly
                            ? _definition.ResetWeekday
                            : null
                    ),
                    rewardIds,
                    Actor()
                ),
                CancellationToken.None
            )
        );
        if (!_failed)
        {
            _definition = new();
        }
    }

    private async Task TransitionAsync(
        CommunitySeasonView season,
        CommunitySeasonTransition transition
    ) =>
        await MutateAsync(() =>
            _progression.TransitionSeasonAsync(
                HostId,
                new(Guid.NewGuid(), season.Id, season.Revision, transition, Actor(), string.Empty),
                CancellationToken.None
            )
        );

    private async Task EditScheduleAsync(CommunityDefinitionView definition)
    {
        var edit = ScheduleFor(definition);
        if (!TimeOnly.TryParseExact(edit.LocalTime, "HH:mm", out var localTime))
        {
            Fail("Local reset time must use HH:mm.");
            return;
        }
        await MutateAsync(() =>
            _progression.EditScheduleAsync(
                HostId,
                new(
                    Guid.NewGuid(),
                    definition.Id,
                    new(
                        edit.Cadence,
                        localTime,
                        edit.Cadence == CommunityResetCadence.Weekly ? edit.Weekday : null
                    ),
                    edit.Confirmed,
                    Actor(),
                    "Confirmed active schedule rollover"
                ),
                CancellationToken.None
            )
        );
    }

    private async Task MutateAsync(Func<Task<CommunityOperationOutcome>> operation) =>
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await operation();
                (_failed, _feedback) = result switch
                {
                    CommunityOperationOutcome.Succeeded { WasIdempotent: true } => (
                        false,
                        "That operation was already applied."
                    ),
                    CommunityOperationOutcome.Succeeded => (false, "Community progression saved."),
                    CommunityOperationOutcome.Conflict conflict => (true, conflict.Message),
                    CommunityOperationOutcome.Invalid invalid => (true, invalid.Message),
                    CommunityOperationOutcome.NotFound => (
                        true,
                        "Community progression item not found."
                    ),
                    _ => (true, "Community progression is disabled."),
                };
                await LoadAsync();
            }
        );

    private ScheduleEditDraft ScheduleFor(CommunityDefinitionView definition)
    {
        if (_scheduleEdits.TryGetValue(definition.Id.Value, out var value))
        {
            return value;
        }
        value = new()
        {
            Cadence = definition.Schedule.Cadence,
            LocalTime = definition.Schedule.LocalTime.ToString(
                "HH:mm",
                CultureInfo.InvariantCulture
            ),
            Weekday = definition.Schedule.Weekday ?? DayOfWeek.Monday,
        };
        _scheduleEdits[definition.Id.Value] = value;
        return value;
    }

    private static string StageKey(CommunitySeasonView season, string stage) =>
        $"season-{season.Id.Value:N}-{stage}";

    private bool StageOpen(CommunitySeasonView season, string stage) =>
        !_closedStages.Contains(StageKey(season, stage));

    private void SetStageOpen(CommunitySeasonView season, string stage, bool open)
    {
        var key = StageKey(season, stage);
        _ = open ? _closedStages.Remove(key) : _closedStages.Add(key);
    }

    private static string SeasonSummary(CommunitySeasonView season)
    {
        var range = CommunityProgressionPresentation.SeasonRange(
            season.StartsAtUtc,
            season.EndsAtUtc
        );
        return season.Status switch
        {
            CommunitySeasonStatus.Draft => $"{range}. Being set up, not visible to viewers yet.",
            CommunitySeasonStatus.Closed => $"{range}. Closed, standings snapshotted.",
            CommunitySeasonStatus.Archived =>
                $"{range}. Archived, final standings and completion history retained.",
            _ => string.IsNullOrWhiteSpace(season.Description) ? range : season.Description,
        };
    }

    private static string? SeasonTimeZone(CommunitySeasonView season) =>
        season
            .Definitions.Select(value => value.TimeZoneId)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string RewardSummary(CommunitySeasonView season) =>
        season.Rewards.Count == 0 ? "None yet" : $"{season.Rewards.Count} defined";

    private static string DefinitionSummary(CommunitySeasonView season) =>
        season.Definitions.Count == 1 ? "1 definition" : $"{season.Definitions.Count} definitions";

    private static string DefinitionShape(CommunityDefinitionView definition) =>
        string.Join(
            " · ",
            CommunityProgressionPresentation.ScopeLabel(definition.Scope),
            CommunityProgressionPresentation.CompletionLabel(definition.CompletionMode),
            CommunityEventRuleCatalog.Describe(definition.EventRule).Label
        );

    private static string DefinitionCadence(CommunityDefinitionView definition)
    {
        var localTime = definition.Schedule.LocalTime.ToString(
            "HH:mm",
            CultureInfo.InvariantCulture
        );
        var when =
            definition.Schedule.Cadence == CommunityResetCadence.Weekly
                ? $"{definition.Schedule.Weekday ?? DayOfWeek.Monday} {localTime}"
                : localTime;
        var next = definition.NextResetUtc is { } value
            ? $", next {CommunityProgressionPresentation.HumanMoment(value.UtcDateTime)}"
            : string.Empty;
        return $"{definition.Schedule.Cadence}, resets {when} ({definition.TimeZoneId}){next}";
    }

    private CommunityActor Actor() => new(PageContext.Session.UserId, PageContext.Session.Login);

    private void Fail(string message)
    {
        _failed = true;
        _feedback = message;
    }

    private static bool TryUtc(string value, out DateTime result)
    {
        if (
            DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
        )
        {
            result = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }
        result = default;
        return false;
    }

    private sealed class SeasonDraft
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ModeratorNotes { get; set; } = string.Empty;
        public CommunityVisibility Visibility { get; set; } = CommunityVisibility.Public;
        public string StartsAtUtc { get; set; } = string.Empty;
        public string EndsAtUtc { get; set; } = string.Empty;

        public static SeasonDraft New() =>
            new()
            {
                StartsAtUtc = DateTime.UtcNow.ToString(
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture
                ),
                EndsAtUtc = DateTime
                    .UtcNow.AddDays(30)
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            };
    }

    private sealed class RewardDraft
    {
        public string Key { get; set; } = string.Empty;
        public CommunityRewardKind Kind { get; set; } = CommunityRewardKind.Title;
        public string Name { get; set; } = string.Empty;
        public string PresentationToken { get; set; } = string.Empty;
    }

    private sealed class DefinitionDraft
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CommunityDefinitionKind Kind
        {
            get;
            set
            {
                field = value;
                if (value == CommunityDefinitionKind.Achievement)
                {
                    Completion = CommunityCompletionMode.OneTime;
                }
                EnsureAvailableRule();
            }
        } = CommunityDefinitionKind.Quest;
        public CommunityProgressScope Scope
        {
            get;
            set
            {
                field = value;
                EnsureAvailableRule();
            }
        } = CommunityProgressScope.Viewer;
        public CommunityCompletionMode Completion { get; set; } = CommunityCompletionMode.OneTime;
        public CommunityEventRuleKind EventRule { get; set; } = CommunityEventRuleKind.ChatMessage;
        public CommunityProgressIncrement Increment { get; set; } =
            CommunityProgressIncrement.Occurrence;
        public string FilterToken { get; set; } = string.Empty;
        public string Target { get; set; } = "1";
        public string PointsReward { get; set; } = "0";
        public string RewardKeys { get; set; } = string.Empty;
        public CommunityResetCadence ResetCadence { get; set; } = CommunityResetCadence.None;
        public string ResetLocalTime { get; set; } = "00:00";
        public DayOfWeek ResetWeekday { get; set; } = DayOfWeek.Monday;

        private void EnsureAvailableRule()
        {
            var available = CommunityEventRuleCatalog.AvailableFor(Kind, Scope);
            if (!available.Any(value => value.Kind == EventRule))
            {
                EventRule = available[0].Kind;
            }
        }
    }

    private sealed class ScheduleEditDraft
    {
        public CommunityResetCadence Cadence { get; set; }
        public string LocalTime { get; set; } = "00:00";
        public DayOfWeek Weekday { get; set; } = DayOfWeek.Monday;
        public bool Confirmed { get; set; }
    }
}
