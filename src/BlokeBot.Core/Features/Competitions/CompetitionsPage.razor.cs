using System.Diagnostics;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Competitions;

public partial class CompetitionsPage
{
    private IReadOnlyList<CompetitionModeratorView> _competitions = [];
    private CompetitionDraftModel _draft = new();
    private RegistrationModel _registration = new();
    private readonly Dictionary<Guid, ResultModel> _results = [];
    private readonly Dictionary<Guid, string> _notes = [];
    private CompetitionId? _selectedCompetitionId;
    private CompetitionMatchId? _selectedMatchId;
    private string _activeTab = "standings";
    private bool _isCreating;
    private bool _featureEnabled;
    private bool _failed;
    private string _feedback = string.Empty;

    private string _publicUrl => $"/competitions/{Uri.EscapeDataString(HostLogin)}";
    private CompetitionModeratorView? _selected =>
        _selectedCompetitionId is { } id
            ? _competitions.SingleOrDefault(x => x.Competition.Id == id)
            : null;
    private CompetitionMatchView? _selectedMatch =>
        _selectedMatchId is { } id
            ? _selected?.Competition.Matches.SingleOrDefault(x => x.Id == id)
            : null;
    private static IReadOnlyList<SegmentedTabItem> _tabs { get; } =
    [
        new("standings", "Standings"),
        new("schedule", "Schedule"),
        new("entrants", "Entrants"),
        new("settings", "Settings & history"),
    ];

    protected override async Task OnInitializedAsync()
    {
        _draft = CompetitionDraftModel.New(_clock.GetUtcNow().UtcDateTime);
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
        _featureEnabled = configured.Contains(HostFeatureFlags.Competitions);
        _competitions = _featureEnabled
            ? await _service.GetModeratorAsync(HostId, CancellationToken.None)
            : [];
        ReconcileSelection();
    }

    private Task CreateAsync() =>
        MutateAsync(async () =>
        {
            var outcome = await _service.CreateAsync(
                HostId,
                new(
                    Guid.NewGuid(),
                    _draft.Name,
                    _draft.Description,
                    _draft.Format,
                    _draft.EntryKind,
                    _draft.Seeding,
                    _draft.Tiebreak,
                    _draft.Capacity,
                    _draft.TeamSize,
                    PointAmount.ParseAbsolute(_draft.MinimumPoints),
                    _draft.WinPoints,
                    _draft.DrawPoints,
                    _draft.LossPoints,
                    _draft.Seed,
                    _draft.ReminderHoursBefore,
                    _draft.ReminderMessage,
                    PointAmount.ParseAbsolute(_draft.WinnerPoints),
                    PointAmount.ParseAbsolute(_draft.RunnerUpPoints),
                    _draft.WinnerAchievement,
                    _draft.RunnerUpAchievement,
                    _draft
                        .MilestoneRewards.Select(x => new CompetitionMilestoneRewardDraft(
                            x.WinsRequired,
                            PointAmount.ParseAbsolute(x.Points),
                            x.AchievementKey
                        ))
                        .ToArray(),
                    _draft.PrivateLobbyInformation,
                    Actor(),
                    _draft.PrivateReason
                ),
                CancellationToken.None
            );
            if (outcome is CompetitionOutcome.Succeeded)
            {
                _isCreating = false;
                _selectedCompetitionId = null;
                _draft = CompetitionDraftModel.New(_clock.GetUtcNow().UtcDateTime);
            }
            await FinishAsync(outcome, "Competition created.");
        });

    private Task TransitionAsync(CompetitionView competition, string transition) =>
        MutateAsync(async () =>
        {
            var command = new CompetitionTransition(
                Guid.NewGuid(),
                competition.Id,
                competition.Revision,
                Actor(),
                Note(competition.Id.Value)
            );
            var outcome = transition switch
            {
                "open" => await _service.OpenRegistrationAsync(
                    HostId,
                    command,
                    CancellationToken.None
                ),
                "start" => await _service.StartAsync(
                    HostId,
                    command,
                    _clock.GetUtcNow().UtcDateTime.AddDays(1),
                    CancellationToken.None
                ),
                "complete" => await _service.CompleteAsync(HostId, command, CancellationToken.None),
                "archive" => await _service.ArchiveAsync(HostId, command, CancellationToken.None),
                _ => throw new UnreachableException(),
            };
            await FinishAsync(
                outcome,
                transition switch
                {
                    "open" => "Registration opened.",
                    "start" => "Competition started and schedule generated.",
                    "complete" => "Competition completed and placement rewards reconciled.",
                    _ => "Competition archived.",
                }
            );
        });

    private Task RegisterAsync(CompetitionView competition) =>
        MutateAsync(async () =>
        {
            var members = Enumerable
                .Range(0, competition.TeamSize)
                .Select(index => Member(index))
                .Select(x => new CompetitionMember(
                    x.TwitchUserId,
                    x.Login,
                    x.DisplayName,
                    x.PrivateContact
                ))
                .ToArray();
            var outcome = await _service.RegisterAsync(
                HostId,
                new(
                    Guid.NewGuid(),
                    competition.Id,
                    _registration.Name,
                    _registration.SeedRank,
                    members,
                    Actor(),
                    _registration.PrivateReason
                ),
                CancellationToken.None
            );
            await FinishAsync(outcome, "Entrant registered.");
            if (outcome is CompetitionOutcome.Succeeded)
            {
                _registration = new();
            }
        });

    private Task ConfirmAsync(CompetitionView competition, CompetitionMatchView match) =>
        MutateAsync(async () =>
        {
            var result = Result(match);
            var outcome = await _service.ConfirmResultAsync(
                HostId,
                new(
                    Guid.NewGuid(),
                    competition.Id,
                    match.Id,
                    competition.Revision,
                    result.ScoreA,
                    result.ScoreB,
                    Actor(),
                    result.PrivateReason
                ),
                CancellationToken.None
            );
            await FinishAsync(
                outcome,
                match.Status == CompetitionMatchStatus.Confirmed
                    ? "Result corrected; downstream outcomes recomputed."
                    : "Result confirmed."
            );
        });

    private void NewCompetition()
    {
        _isCreating = true;
        _feedback = string.Empty;
    }

    private void SelectCompetition(CompetitionId id)
    {
        _isCreating = false;
        _selectedCompetitionId = id;
        _registration = new();
        ReconcileSelectedMatch();
    }

    private void SelectMobileCompetition(string? value)
    {
        if (Guid.TryParse(value, out var id))
        {
            SelectCompetition(new(id));
        }
        else
        {
            NewCompetition();
        }
    }

    private void SelectMatch(CompetitionMatchId id)
    {
        _selectedMatchId = id;
        _activeTab = "schedule";
    }

    private void ReconcileSelection()
    {
        if (!_featureEnabled)
        {
            _selectedCompetitionId = null;
            _selectedMatchId = null;
            return;
        }
        if (_competitions.Count == 0)
        {
            _isCreating = true;
            _selectedCompetitionId = null;
            _selectedMatchId = null;
            return;
        }
        if (
            _selectedCompetitionId is null
            || _competitions.All(x => x.Competition.Id != _selectedCompetitionId)
        )
        {
            _selectedCompetitionId = _competitions[0].Competition.Id;
        }
        ReconcileSelectedMatch();
    }

    private void ReconcileSelectedMatch()
    {
        var matches = _selected?.Competition.Matches ?? [];
        if (_selectedMatchId is not null && matches.Any(x => x.Id == _selectedMatchId))
        {
            return;
        }
        var selected = matches.FirstOrDefault(x =>
            x.Status == CompetitionMatchStatus.Pending
            && x.EntrantAId is not null
            && x.EntrantBId is not null
        );
        selected ??= matches.LastOrDefault(x => x.Status == CompetitionMatchStatus.Confirmed);
        _selectedMatchId = selected?.Id;
    }

    private static int CurrentRound(CompetitionView competition) =>
        competition
            .Matches.Where(x => x.Status == CompetitionMatchStatus.Pending)
            .Select(x => x.Round)
            .DefaultIfEmpty(competition.Matches.Select(x => x.Round).DefaultIfEmpty(0).Max())
            .Min();

    private static int TotalRounds(CompetitionView competition) =>
        competition.Matches.Select(x => x.Round).DefaultIfEmpty(0).Max();

    private static string StatusPillClass(CompetitionStatus status) =>
        status switch
        {
            CompetitionStatus.Running => "status-pill status-pill--green",
            CompetitionStatus.Registration => "status-pill status-pill--blue",
            CompetitionStatus.Completed => "status-pill status-pill--violet",
            _ => "status-pill status-pill--slate",
        };

    private void AddMilestoneReward()
    {
        if (_draft.MilestoneRewards.Count < 8)
        {
            _draft.MilestoneRewards.Add(new());
        }
    }

    private void RemoveMilestoneReward(MilestoneRuleModel rule) =>
        _ = _draft.MilestoneRewards.Remove(rule);

    private async Task MutateAsync(Func<Task> mutation)
    {
        try
        {
            await RunSelectedHostMutationAsync(HostId, mutation);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            Fail(exception.Message);
        }
    }

    private async Task FinishAsync(CompetitionOutcome outcome, string success)
    {
        switch (outcome)
        {
            case CompetitionOutcome.Succeeded:
                _failed = false;
                _feedback = success;
                await LoadAsync();
                break;
            case CompetitionOutcome.Invalid invalid:
                Fail(invalid.Message);
                break;
            case CompetitionOutcome.Conflict conflict:
                Fail(conflict.Message);
                break;
            case CompetitionOutcome.FeatureDisabled:
                Fail("Tournaments & leagues is turned off.");
                break;
            default:
                Fail("Competition is no longer available.");
                break;
        }
    }

    private void Fail(string message)
    {
        _failed = true;
        _feedback = message;
    }

    private CompetitionActor Actor() => new(PageContext.Session.UserId, PageContext.Session.Login);

    private string Note(Guid id) => _notes.GetValueOrDefault(id, string.Empty);

    private void SetNote(Guid id, string value) => _notes[id] = value;

    private ResultModel Result(CompetitionMatchView match) =>
        _results.TryGetValue(match.Id.Value, out var result)
            ? result
            : _results[match.Id.Value] = new()
            {
                ScoreA = match.ScoreA ?? 0,
                ScoreB = match.ScoreB ?? 0,
            };

    private MemberModel Member(int index)
    {
        while (_registration.Members.Count <= index)
        {
            _registration.Members.Add(new());
        }
        return _registration.Members[index];
    }

    private sealed class CompetitionDraftModel
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CompetitionFormat Format { get; set; } = CompetitionFormat.RoundRobin;
        public CompetitionEntryKind EntryKind { get; set; } = CompetitionEntryKind.Individual;
        public CompetitionSeeding Seeding { get; set; } = CompetitionSeeding.Random;
        public CompetitionTiebreak Tiebreak { get; set; } =
            CompetitionTiebreak.ScoreDifferenceThenScoreFor;
        public int Capacity { get; set; } = 8;
        public int TeamSize { get; set; } = 2;
        public string MinimumPoints { get; set; } = "0";
        public int WinPoints { get; set; } = 3;
        public int DrawPoints { get; set; } = 1;
        public int LossPoints { get; set; }
        public string Seed { get; set; } = string.Empty;
        public int ReminderHoursBefore { get; set; } = 24;
        public string ReminderMessage { get; set; } =
            "Reminder: {competition} round {round} is scheduled for {scheduled}. {public_url}";
        public string WinnerPoints { get; set; } = "500";
        public string RunnerUpPoints { get; set; } = "250";
        public string WinnerAchievement { get; set; } = string.Empty;
        public string RunnerUpAchievement { get; set; } = string.Empty;
        public List<MilestoneRuleModel> MilestoneRewards { get; set; } = [];
        public string PrivateLobbyInformation { get; set; } = string.Empty;
        public string PrivateReason { get; set; } = string.Empty;

        public static CompetitionDraftModel New(DateTime now) =>
            new() { Seed = $"competition-{now:yyyyMMdd-HHmm}" };
    }

    private sealed class MilestoneRuleModel
    {
        public int WinsRequired { get; set; } = 1;
        public string Points { get; set; } = "0";
        public string AchievementKey { get; set; } = string.Empty;
    }

    private sealed class RegistrationModel
    {
        public string Name { get; set; } = string.Empty;
        public int? SeedRank { get; set; }
        public string PrivateReason { get; set; } = string.Empty;
        public List<MemberModel> Members { get; set; } = [new(), new()];
    }

    private sealed class MemberModel
    {
        public string TwitchUserId { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PrivateContact { get; set; } = string.Empty;
    }

    private sealed class ResultModel
    {
        public int ScoreA { get; set; }
        public int ScoreB { get; set; }
        public string PrivateReason { get; set; } = string.Empty;
    }
}
