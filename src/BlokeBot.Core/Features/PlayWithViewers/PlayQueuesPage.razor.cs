using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.PlayWithViewers;

public partial class PlayQueuesPage
{
    private IReadOnlyList<PlayQueueSummary> _queueList = [];
    private ModeratorPlayQueuePage? _moderatorPage;
    private readonly Dictionary<long, EntryDraft> _entryDrafts = [];
    private QueueDraft _draft = QueueDraft.New();
    private Guid? _selectedFieldIdentity;
    private Guid? _fieldFocusIdentity;
    private long? _activeEntryEditorId;
    private string _feedback = string.Empty;
    private string _lobbyCode = string.Empty;
    private string _roleDraft = string.Empty;
    private long _fieldFocusRequest;
    private long _primaryFocusRequest;
    private bool _isCreating = true;
    private bool _operationFailed;
    private bool _featureEnabled;
    private PlayQueuePane _pane = PlayQueuePane.Setup;
    private readonly StudioOpenSet<PlayQueueStage> _openStages = new(PlayQueueStage.Basics);
    private readonly StudioOpenSet<long> _openEntryFolds = new();

    private const string _formPreviewBox =
        "overflow-hidden rounded-lg border border-[var(--app-control-border)] bg-[var(--app-control-bg)] px-[0.55rem] py-[0.32rem] text-[0.78rem] whitespace-nowrap text-ellipsis text-[var(--app-placeholder)]";

    private enum PlayQueuePane
    {
        Setup,
        Run,
    }

    private enum PlayQueueStage
    {
        Basics,
        PartyAndFairPicks,
        Questions,
        TimingAndVisibility,
    }

    private sealed record SelectionChoice(
        PlayQueueSelectionMode Mode,
        string Title,
        string Description
    );

    private static readonly IReadOnlyList<SelectionChoice> _selectionChoices =
    [
        new(
            PlayQueueSelectionMode.JoinOrder,
            "First to join",
            "Simple queue order: higher priority, then earliest join."
        ),
        new(
            PlayQueueSelectionMode.LeastRecentParticipation,
            "Least recently played",
            "Viewers who have not played with you lately jump ahead, so regulars do not hog every game."
        ),
    ];

    private string _publicUrl =>
        $"/queues/{Uri.EscapeDataString(HostLogin)}/{Uri.EscapeDataString(_draft.Slug)}";

    private string _slugOrExample => string.IsNullOrWhiteSpace(_draft.Slug) ? "duos" : _draft.Slug;

    private string _nameOrExample =>
        string.IsNullOrWhiteSpace(_draft.Name) ? "Ranked Duos" : _draft.Name;

    private string _viewerPagePath => $"/queues/{HostLogin}/{_slugOrExample}";

    private IReadOnlyList<StudioRailGroup> _railGroups =>
        [
            new(
                "Queues",
                [
                    .. _queueList.Select(queue => new StudioRailItem
                    {
                        Key = queue.Slug,
                        Label = queue.Slug,
                        Search = $"{queue.Slug} {queue.Name}",
                        Sub = string.IsNullOrWhiteSpace(queue.Name) ? null : queue.Name,
                        Meta = queue.IsOpen ? null : "closed",
                        Monospace = true,
                        On = queue.IsOpen,
                        Selected = !_isCreating && queue.Slug == _draft.Slug,
                        Select = EventCallback.Factory.Create(
                            this,
                            () => SelectQueueAsync(queue.Slug)
                        ),
                    }),
                ],
                "No saved queues yet."
            ),
        ];

    private IReadOnlyList<StudioSegmentedOption<PlayQueuePane>> _paneOptions =>
        [
            new(PlayQueuePane.Setup, "Set up queue"),
            new(
                PlayQueuePane.Run,
                _moderatorPage is { Waiting.Count: > 0 } moderation
                    ? $"Run the queue · {moderation.Waiting.Count}"
                    : "Run the queue"
            ),
        ];

    private string _headerTitle =>
        _isCreating ? "New queue (not saved)"
        : string.IsNullOrWhiteSpace(_draft.Name) ? _draft.Slug
        : _draft.Name;

    private string? _headerStats =>
        _moderatorPage is { } moderation
            ? $"/{_draft.Slug} · {moderation.Waiting.Count} waiting · party {moderation.CurrentParty.Count} of {moderation.Queue.Capacity}"
            : null;

    private string _basicsSummary =>
        string.IsNullOrWhiteSpace(_draft.Activity)
            ? $"/{_slugOrExample}"
            : $"/{_slugOrExample} · {_draft.Activity}";

    private string _partySummary =>
        $"Party of {_draft.Capacity} · {(_draft.SelectionMode == PlayQueueSelectionMode.JoinOrder ? "first to join picked first" : "least-recent players first")} · {(_roleSections.Count == 0 ? "no role targets" : Count(_roleSections.Count, "role target"))}";

    private string _questionsSummary =>
        _draft.Fields.Count == 0
            ? "no questions · viewers just join with their name"
            : $"{Count(_draft.Fields.Count, "question")} · all optional, shown publicly";

    private string _timingSummary =>
        $"{ReadyProse()} to ready up · {_draft.SkipExclusion} min sit-out · {(_draft.ShowNames ? "names shown" : "names hidden")}";

    private string ReadyProse() =>
        !int.TryParse(
            _draft.ReadinessTimeout,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var seconds
        )
            ? $"{_draft.ReadinessTimeout} s"
            : DurationProse.Format(seconds);

    private static string Count(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private IReadOnlyList<string> _roleSections =>
        _draft.RoleRequirements.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

    private static string RoleChipLabel(string section) => section.Replace("=", " × ");

    private void AddRole()
    {
        var value = _roleDraft.Trim();
        if (value.Length == 0 || _roleSections.Count >= PlayQueueLimits.MaximumRoles)
        {
            return;
        }

        var pair = value.Split('=', 2, StringSplitOptions.TrimEntries);
        var count = 1;
        if (
            pair[0].Length == 0
            || (
                pair.Length == 2
                && !int.TryParse(
                    pair[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out count
                )
            )
        )
        {
            return;
        }

        _draft.RoleRequirements = AppendedList(_roleSections, $"{pair[0]}={count}");
        _roleDraft = string.Empty;
    }

    private void AddRoleOnEnter(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            AddRole();
        }
    }

    private void RemoveRole(int index) =>
        _draft.RoleRequirements = string.Join(
            ", ",
            _roleSections.Where((_, position) => position != index)
        );

    private static string AppendedList(IReadOnlyList<string> current, string value) =>
        current.Count == 0 ? value : $"{string.Join(", ", current)}, {value}";

    private static IReadOnlyList<string> FieldChoices(FieldDraft field) =>
        field.Choices.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

    private static void AddChoice(FieldDraft field)
    {
        var value = field.ChoiceDraft.Trim();
        if (value.Length == 0)
        {
            return;
        }

        field.Choices = AppendedList(FieldChoices(field), value);
        field.ChoiceDraft = string.Empty;
    }

    private static void AddChoiceOnEnter(FieldDraft field, KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            AddChoice(field);
        }
    }

    private static void RemoveChoice(FieldDraft field, string choice) =>
        field.Choices = string.Join(", ", FieldChoices(field).Where(value => value != choice));

    private static string KeyOrExample(FieldDraft field) =>
        string.IsNullOrWhiteSpace(field.Key) ? "platform" : field.Key;

    private static string FieldPreviewPlaceholder(FieldDraft field) =>
        FieldChoices(field).Count > 0 ? "Choose… ▾" : "Free text";

    private static string EntryStatusPillClass(PlayQueueEntryStatus status) =>
        status is PlayQueueEntryStatus.Ready or PlayQueueEntryStatus.Selected
            ? "status-pill bg-[var(--app-affirmative-surface)] text-[var(--app-affirmative)]"
            : "status-pill bg-[var(--app-surface-muted)] text-[var(--app-text-muted)] ring-1 ring-[var(--app-border)]";

    private static string FairOrderProse(PlayQueueSelectionMode mode) =>
        mode == PlayQueueSelectionMode.JoinOrder
            ? "Picked in order: higher priority first, then join time."
            : "Picked fairly: higher priority first, then whoever played with you least recently, then join time.";

    private static string ParticipationProse(ModeratorPlayQueueEntryView entry) =>
        entry.LastParticipatedAtUtc is { } last
            ? $"last played {last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : "never played with you";

    private IReadOnlyList<StudioChatLine> ChatPreviewLines()
    {
        var arguments = string.Join(
            " ",
            _draft
                .Fields.Where(field => !string.IsNullOrWhiteSpace(field.Key))
                .Select(field => (field.Key, Choices: FieldChoices(field)))
                .Where(field => field.Choices.Count > 0)
                .Take(2)
                .Select(field => $"{field.Key}={field.Choices[0]}")
        );
        return
        [
            new()
            {
                Speaker = "gazza_plays",
                SpeakerColour = "#1e90ff",
                Message =
                    arguments.Length == 0
                        ? $"!join {_slugOrExample}"
                        : $"!join {_slugOrExample} {arguments}",
                Monospace = true,
            },
            new()
            {
                Speaker = "BlokeBot",
                SpeakerColour = "#00ad6f",
                Badge = "BOT",
                Bot = true,
                Message = $"You joined {_nameOrExample} at position 5.",
            },
            new()
            {
                Speaker = "pixel_penny",
                SpeakerColour = "#e91e63",
                Message = "!position",
                Monospace = true,
            },
            new()
            {
                Speaker = "BlokeBot",
                SpeakerColour = "#00ad6f",
                Badge = "BOT",
                Bot = true,
                Message = "You are in the current party.",
            },
        ];
    }

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        _featureEnabled =
            HostId != 0
            && await _features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.PlayWithViewers,
                CancellationToken.None
            );
        if (!_featureEnabled)
        {
            return;
        }
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (HostId == 0)
        {
            return;
        }

        _queueList = await _queues.GetQueuesForHostAsync(HostId, CancellationToken.None);
        if (_queueList.Count > 0 && string.IsNullOrWhiteSpace(_draft.Slug))
        {
            await SelectQueueAsync(_queueList[0].Slug);
            return;
        }

        if (_queueList.Count == 0 && _isCreating)
        {
            EnsureFieldSelection();
            SetCreateGuidance();
            _primaryFocusRequest++;
        }
    }

    private async Task SelectQueueAsync(string slug)
    {
        _draft = QueueDraft.From(_queueList.Single(value => value.Slug == slug));
        _isCreating = false;
        _operationFailed = false;
        _feedback = string.Empty;
        _pane = PlayQueuePane.Setup;
        _openEntryFolds.Reset();
        _activeEntryEditorId = null;
        SelectFirstField();
        await RefreshPageAsync();
    }

    private async Task RefreshPageAsync()
    {
        _moderatorPage = string.IsNullOrWhiteSpace(_draft.Slug)
            ? null
            : await _queues.GetModeratorPageAsync(HostId, _draft.Slug, CancellationToken.None);
        _entryDrafts.Clear();
        if (_moderatorPage is not null)
        {
            foreach (var entry in _moderatorPage.Waiting.Concat(_moderatorPage.CurrentParty))
            {
                _entryDrafts[entry.EntryId] = EntryDraft.From(entry);
            }
        }
    }

    private void NewQueue()
    {
        if (_isCreating)
        {
            return;
        }

        _draft = QueueDraft.New();
        _isCreating = true;
        _moderatorPage = null;
        _entryDrafts.Clear();
        _operationFailed = false;
        _pane = PlayQueuePane.Setup;
        _openEntryFolds.Reset();
        _activeEntryEditorId = null;
        SelectFirstField();
        SetCreateGuidance();
        _primaryFocusRequest++;
    }

    private void AddField()
    {
        if (_draft.Fields.Count >= PlayQueueLimits.MaximumFields)
        {
            return;
        }

        var field = FieldDraft.New();
        _draft.Fields.Add(field);
        SelectField(field);
    }

    private void RemoveField(FieldDraft field)
    {
        var removedIndex = _draft.Fields.IndexOf(field);
        if (removedIndex < 0)
        {
            return;
        }

        _draft.Fields.RemoveAt(removedIndex);
        if (_draft.Fields.Count == 0)
        {
            _selectedFieldIdentity = null;
            _fieldFocusIdentity = null;
            return;
        }

        var neighbour = _draft.Fields[Math.Min(removedIndex, _draft.Fields.Count - 1)];
        SelectField(neighbour);
    }

    private Task SaveAsync() =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var wasCreating = _isCreating;
                var command = _draft.ToCommand();
                if (command is null)
                {
                    Fail(
                        "Enter whole numbers for party size, ready-check time, history, no-show wait, and party role targets."
                    );
                    return;
                }

                var result = await _queues.ConfigureAsync(HostId, command, CancellationToken.None);
                switch (result)
                {
                    case PlayQueueResult<PlayQueueSummary>.Succeeded succeeded:
                        _draft = QueueDraft.From(succeeded.Value);
                        _isCreating = false;
                        _operationFailed = false;
                        _feedback = wasCreating ? "Queue created." : "Queue saved.";
                        SelectFirstField();
                        await LoadAsync();
                        await RefreshPageAsync();
                        break;
                    case PlayQueueResult<PlayQueueSummary>.Rejected rejected:
                        _operationFailed = true;
                        _feedback = rejected.Reason.Message;
                        break;
                }
            }
        );

    private void SetCreateGuidance() =>
        _feedback = "New queue ready. Complete its details, then Save queue to create it.";

    private void SelectFirstField()
    {
        _selectedFieldIdentity = _draft.Fields.FirstOrDefault()?.Identity;
        _fieldFocusIdentity = null;
    }

    private void EnsureFieldSelection()
    {
        if (
            _selectedFieldIdentity is null
            || _draft.Fields.All(field => field.Identity != _selectedFieldIdentity)
        )
        {
            SelectFirstField();
        }
    }

    private void SelectField(FieldDraft field)
    {
        _selectedFieldIdentity = field.Identity;
        _fieldFocusIdentity = field.Identity;
        _fieldFocusRequest++;
    }

    private bool IsFieldSelected(FieldDraft field) => field.Identity == _selectedFieldIdentity;

    private long FieldFocusRequest(FieldDraft field) =>
        field.Identity == _fieldFocusIdentity ? _fieldFocusRequest : 0;

    private static string FieldInventoryLabelId(FieldDraft field) =>
        $"queue-field-{field.Identity:N}-inventory-label";

    private static string FieldEditorRegionId(FieldDraft field) =>
        $"queue-field-{field.Identity:N}-editor";

    private static string FieldDisplayName(FieldDraft field) =>
        string.IsNullOrWhiteSpace(field.Label) ? "Untitled field" : field.Label;

    private static string QueueFieldSummary(FieldDraft field)
    {
        var key = string.IsNullOrWhiteSpace(field.Key) ? "no key" : field.Key;
        var choices = FieldChoices(field);
        return choices.Count == 0
            ? $"{key} · free text"
            : $"{key} · pick from {Count(choices.Count, "choice")}";
    }

    internal static string EntryStatusLabel(PlayQueueEntryStatus status) =>
        status switch
        {
            PlayQueueEntryStatus.Waiting => "Waiting",
            PlayQueueEntryStatus.AwaitingReady => "Awaiting response",
            PlayQueueEntryStatus.Ready => "Ready",
            PlayQueueEntryStatus.Selected => "Selected",
            PlayQueueEntryStatus.Left => "Left queue",
            PlayQueueEntryStatus.Skipped => "Skipped",
            PlayQueueEntryStatus.NoShow => "Did not respond",
            _ => throw new UnreachableException("Unknown play queue entry status."),
        };

    private Task ToggleOpenAsync() =>
        Run(async () =>
        {
            var result = await _queues.SetOpenAsync(
                HostId,
                _draft.Slug,
                !_draft.IsOpen,
                CancellationToken.None
            );
            _feedback = result.Match(
                succeeded =>
                {
                    _draft.IsOpen = succeeded.Value.IsOpen;
                    return succeeded.Value.IsOpen ? "Queue opened." : "Queue closed.";
                },
                rejected => rejected.Reason.Message
            );
            _operationFailed = result is PlayQueueResult<PlayQueueSummary>.Rejected;
        });

    private Task SelectPartyAsync() => SelectAsync(false);

    private Task KeepPartyAsync() => SelectAsync(true);

    private Task SelectAsync(bool keep) =>
        Run(async () =>
        {
            var result = await _queues.SelectPartyAsync(
                HostId,
                _draft.Slug,
                keep,
                CancellationToken.None
            );
            _feedback = result.Match(
                succeeded =>
                    keep ? "Current party kept." : $"Party {succeeded.Value.PartyNumber} selected.",
                rejected => rejected.Reason.Message
            );
            _operationFailed = result is PlayQueueResult<PlayQueueSelection>.Rejected;
        });

    private Task ReadyCheckAsync(long id) =>
        EntryMutationAsync(
            () => _queues.StartReadyCheckAsync(HostId, id, CancellationToken.None),
            "Ready check started."
        );

    private Task SkipAsync(long id) =>
        EntryMutationAsync(
            () => _queues.SkipAsync(HostId, id, CancellationToken.None),
            "Viewer skipped temporarily."
        );

    private Task ReplaceOneAsync(long id) =>
        Run(async () =>
        {
            var result = await _queues.ReplaceOneAsync(HostId, id, CancellationToken.None);
            _feedback = result.Match(
                _ => "Party member replaced.",
                rejected => rejected.Reason.Message
            );
            _operationFailed = result is PlayQueueResult<PlayQueueSelection>.Rejected;
        });

    private Task NoShowAsync(long id) =>
        EntryMutationAsync(
            () => _queues.MarkNoShowAsync(HostId, id, CancellationToken.None),
            "No-show recorded."
        );

    private Task SaveEntryAsync(long id) =>
        Run(async () =>
        {
            var draft = _entryDrafts[id];
            if (
                !int.TryParse(
                    draft.Priority,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var priority
                )
            )
            {
                Fail("Priority must be a whole number from -1000 to 1000.");
                return;
            }

            var result = await _queues.UpdateEntryAsync(
                HostId,
                id,
                priority,
                draft.PrivateNote,
                CancellationToken.None
            );
            _feedback = result.Match(
                _ => "Viewer priority and private note saved.",
                rejected => rejected.Reason.Message
            );
            _operationFailed = result is PlayQueueResult<ModeratorPlayQueueEntryView>.Rejected;
        });

    private void SetEntryEditorOpen(long id, bool open)
    {
        _openEntryFolds.Set(id, open);
        if (open)
        {
            _activeEntryEditorId = id;
        }
        else if (_activeEntryEditorId == id)
        {
            _activeEntryEditorId = null;
        }
    }

    private Task EntryMutationAsync(
        Func<Task<PlayQueueResult<ModeratorPlayQueueEntryView>>> mutate,
        string message
    ) =>
        Run(async () =>
        {
            var result = await mutate();
            _feedback = result.Match(_ => message, rejected => rejected.Reason.Message);
            _operationFailed = result is PlayQueueResult<ModeratorPlayQueueEntryView>.Rejected;
        });

    private Task DeliverLobbyAsync() =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                if (_moderatorPage is null)
                {
                    return;
                }
                var outcomes = await _lobbyDelivery.DeliverAsync(
                    HostLogin,
                    _lobbyCode,
                    _moderatorPage
                        .CurrentParty.Select(value => new PrivateLobbyRecipient(
                            value.NormalizedLogin,
                            value.TwitchUserId
                        ))
                        .ToArray(),
                    CancellationToken.None
                );
                _lobbyCode = string.Empty;
                var failures = outcomes.Where(value => !value.Delivered).ToArray();
                _operationFailed = failures.Length > 0;
                _feedback =
                    failures.Length == 0
                        ? $"Lobby message privately delivered to {outcomes.Count} viewer(s)."
                        : $"We couldn’t send the lobby message privately to {string.Join(", ", failures.Select(value => $"@{value.Login}"))}. It was not posted publicly.";
            }
        );

    private Task Run(Func<Task> mutation) =>
        RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                await mutation();
                await RefreshPageAsync();
            }
        );

    private void Fail(string value)
    {
        _operationFailed = true;
        _feedback = value;
    }

    private static string FieldSummary(ModeratorPlayQueueEntryView entry) =>
        entry.Public.Fields.Count == 0
            ? "No entry details"
            : string.Join(
                " · ",
                entry.Public.Fields.Select(static value => $"{value.Label}: {value.Value}")
            );

    private sealed class EntryDraft
    {
        public string Priority { get; set; } = "0";
        public string PrivateNote { get; set; } = string.Empty;

        public static EntryDraft From(ModeratorPlayQueueEntryView entry) =>
            new EntryDraft
            {
                Priority = entry.Priority.ToString(CultureInfo.InvariantCulture),
                PrivateNote = entry.PrivateModeratorNote,
            };
    }

    private sealed class QueueDraft
    {
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Activity { get; set; } = string.Empty;
        public string Capacity { get; set; } = "4";
        public bool IsOpen { get; set; }
        public PlayQueueSelectionMode SelectionMode { get; set; } =
            PlayQueueSelectionMode.LeastRecentParticipation;
        public bool ShowNames { get; set; }
        public string ReadinessTimeout { get; set; } = "120";
        public string HistoryRetention { get; set; } = "30";
        public string SkipExclusion { get; set; } = "15";
        public string RoleRequirements { get; set; } = string.Empty;
        public List<FieldDraft> Fields { get; } = [];

        public ConfigurePlayQueueCommand? ToCommand()
        {
            if (
                !int.TryParse(
                    Capacity,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var capacity
                )
                || !int.TryParse(
                    ReadinessTimeout,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var readiness
                )
                || !int.TryParse(
                    HistoryRetention,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var history
                )
                || !int.TryParse(
                    SkipExclusion,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var exclusion
                )
            )
            {
                return null;
            }

            var roles = new List<PlayQueueRoleRequirementCommand>();
            foreach (
                var section in RoleRequirements.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                var pair = section.Split('=', 2, StringSplitOptions.TrimEntries);
                if (
                    pair.Length != 2
                    || !int.TryParse(
                        pair[1],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var count
                    )
                )
                {
                    return null;
                }

                roles.Add(new PlayQueueRoleRequirementCommand(pair[0], count));
            }
            return new ConfigurePlayQueueCommand(
                Slug,
                Name,
                Activity,
                capacity,
                IsOpen,
                SelectionMode,
                ShowNames,
                readiness,
                history,
                exclusion,
                Fields.Select(static value => value.ToCommand()).ToArray(),
                roles
            );
        }

        public static QueueDraft New()
        {
            var value = new QueueDraft();
            value.Fields.AddRange([
                FieldDraft.Standard("platform", "Platform"),
                FieldDraft.Standard("region", "Region"),
                FieldDraft.Standard("rank", "Rank"),
                FieldDraft.Standard("preferred-role", "Preferred role"),
            ]);
            return value;
        }

        public static QueueDraft From(PlayQueueSummary queue)
        {
            var value = new QueueDraft
            {
                Slug = queue.Slug,
                Name = queue.Name,
                Activity = queue.ActivityName,
                Capacity = queue.Capacity.ToString(CultureInfo.InvariantCulture),
                IsOpen = queue.IsOpen,
                SelectionMode = queue.SelectionMode,
                ShowNames = queue.ShowParticipantNames,
                ReadinessTimeout = queue.ReadinessTimeoutSeconds.ToString(
                    CultureInfo.InvariantCulture
                ),
                HistoryRetention = queue.HistoryRetentionDays.ToString(
                    CultureInfo.InvariantCulture
                ),
                SkipExclusion = queue.SkipExclusionMinutes.ToString(CultureInfo.InvariantCulture),
                RoleRequirements = string.Join(
                    ", ",
                    queue.RoleRequirements.Select(static role => $"{role.Role}={role.MinimumCount}")
                ),
            };
            value.Fields.AddRange(queue.Fields.Select(FieldDraft.From));
            return value;
        }
    }

    private sealed class FieldDraft
    {
        public Guid Identity { get; } = Guid.NewGuid();
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Choices { get; set; } = string.Empty;
        public string ChoiceDraft { get; set; } = string.Empty;

        public PlayQueueFieldCommand ToCommand() =>
            new(
                Key,
                Label,
                Choices.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            );

        public static FieldDraft New() => new() { Key = "field", Label = "Field" };

        public static FieldDraft Standard(string key, string label) =>
            new() { Key = key, Label = label };

        public static FieldDraft From(PlayQueueFieldView field) =>
            new()
            {
                Key = field.Key,
                Label = field.Label,
                Choices = string.Join(", ", field.Choices),
            };
    }
}
