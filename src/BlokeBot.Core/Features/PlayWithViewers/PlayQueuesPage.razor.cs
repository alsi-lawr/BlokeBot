using System.Diagnostics;
using System.Globalization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.PlayWithViewers;

public partial class PlayQueuesPage
{
    private IReadOnlyList<PlayQueueSummary> _queueList = [];
    private ModeratorPlayQueuePage? _moderatorPage;
    private readonly Dictionary<long, EntryDraft> _entryDrafts = [];
    private QueueDraft _draft = QueueDraft.New();
    private Guid? _selectedFieldIdentity;
    private Guid? _fieldFocusIdentity;
    private string _feedback = string.Empty;
    private string _lobbyCode = string.Empty;
    private long _fieldFocusRequest;
    private long _primaryFocusRequest;
    private bool _isCreating = true;
    private bool _operationFailed;
    private bool _featureEnabled;

    private string _publicUrl =>
        $"/queues/{Uri.EscapeDataString(HostLogin)}/{Uri.EscapeDataString(_draft.Slug)}";

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
        var key = string.IsNullOrWhiteSpace(field.Key) ? "No key" : field.Key;
        var choices = field.Choices.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var detail = choices.Length == 0 ? "Free text" : $"{choices.Length} choices";
        return $"{key} · Optional · Public · {detail}";
    }

    internal static string SelectionModeLabel(PlayQueueSelectionMode mode) =>
        mode switch
        {
            PlayQueueSelectionMode.JoinOrder => "First to join",
            PlayQueueSelectionMode.LeastRecentParticipation => "Viewers who played least recently",
            _ => throw new UnreachableException("Unknown play queue selection mode."),
        };

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
                entry.Public.Fields.Select(value => $"{value.Label}: {value.Value}")
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
                Fields.Select(value => value.ToCommand()).ToArray(),
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
                    queue.RoleRequirements.Select(role => $"{role.Role}={role.MinimumCount}")
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
