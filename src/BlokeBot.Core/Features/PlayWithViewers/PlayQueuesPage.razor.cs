using System.Globalization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.PlayWithViewers;

public partial class PlayQueuesPage
{
    private IReadOnlyList<PlayQueueSummary> _queueList = [];
    private ModeratorPlayQueuePage? _moderatorPage;
    private readonly Dictionary<long, EntryDraft> _entryDrafts = [];
    private QueueDraft _draft = QueueDraft.New();
    private string _feedback = string.Empty;
    private string _lobbyCode = string.Empty;
    private bool _operationFailed;

    private string _publicUrl =>
        string.IsNullOrWhiteSpace(HostLogin) || string.IsNullOrWhiteSpace(_draft.Slug)
            ? "#"
            : $"/queues/{Uri.EscapeDataString(HostLogin)}/{Uri.EscapeDataString(_draft.Slug)}";

    protected override async Task OnInitializedAsync()
    {
        await LoadPageContextAsync();
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
        }
    }

    private async Task SelectQueueAsync(string slug)
    {
        _draft = QueueDraft.From(_queueList.Single(value => value.Slug == slug));
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
                _entryDrafts[entry.Public.Id] = EntryDraft.From(entry);
            }
        }
    }

    private void NewQueue()
    {
        _draft = QueueDraft.New();
        _moderatorPage = null;
        _feedback = string.Empty;
    }

    private void AddField()
    {
        _draft.Fields.Add(FieldDraft.New());
    }

    private void RemoveField(FieldDraft field)
    {
        _draft.Fields.Remove(field);
    }

    private Task SaveAsync()
    {
        return RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var command = _draft.ToCommand();
                if (command is null)
                {
                    Fail(
                        "Capacity, expiry, retention, exclusion, and roles must contain valid numbers."
                    );
                    return;
                }

                var result = await _queues.ConfigureAsync(HostId, command, CancellationToken.None);
                _feedback = result.Match(
                    succeeded =>
                    {
                        _draft = QueueDraft.From(succeeded.Value);
                        return "Queue saved.";
                    },
                    rejected => rejected.Reason.Message
                );
                _operationFailed = result is PlayQueueResult<PlayQueueSummary>.Rejected;
                await LoadAsync();
                await RefreshPageAsync();
            }
        );
    }

    private Task ToggleOpenAsync()
    {
        return Run(async () =>
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
    }

    private Task SelectPartyAsync()
    {
        return SelectAsync(false);
    }

    private Task KeepPartyAsync()
    {
        return SelectAsync(true);
    }

    private Task SelectAsync(bool keep)
    {
        return Run(async () =>
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
    }

    private Task ReadyCheckAsync(long id)
    {
        return EntryMutationAsync(
            () => _queues.StartReadyCheckAsync(HostId, id, CancellationToken.None),
            "Ready check started."
        );
    }

    private Task SkipAsync(long id)
    {
        return EntryMutationAsync(
            () => _queues.SkipAsync(HostId, id, CancellationToken.None),
            "Viewer skipped temporarily."
        );
    }

    private Task ReplaceOneAsync(long id)
    {
        return Run(async () =>
        {
            var result = await _queues.ReplaceOneAsync(HostId, id, CancellationToken.None);
            _feedback = result.Match(
                _ => "Party member replaced.",
                rejected => rejected.Reason.Message
            );
            _operationFailed = result is PlayQueueResult<PlayQueueSelection>.Rejected;
        });
    }

    private Task NoShowAsync(long id)
    {
        return EntryMutationAsync(
            () => _queues.MarkNoShowAsync(HostId, id, CancellationToken.None),
            "No-show recorded."
        );
    }

    private Task SaveEntryAsync(long id)
    {
        return Run(async () =>
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
    }

    private Task EntryMutationAsync(
        Func<Task<PlayQueueResult<ModeratorPlayQueueEntryView>>> mutate,
        string message
    )
    {
        return Run(async () =>
        {
            var result = await mutate();
            _feedback = result.Match(_ => message, rejected => rejected.Reason.Message);
            _operationFailed = result is PlayQueueResult<ModeratorPlayQueueEntryView>.Rejected;
        });
    }

    private Task DeliverLobbyAsync()
    {
        return RunSelectedHostMutationAsync(
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
                        : $"Private delivery failed for {string.Join(", ", failures.Select(value => $"@{value.Login}"))}. No public fallback was attempted.";
            }
        );
    }

    private Task Run(Func<Task> mutation)
    {
        return RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                await mutation();
                await RefreshPageAsync();
            }
        );
    }

    private void Fail(string value)
    {
        _operationFailed = true;
        _feedback = value;
    }

    private static string FieldSummary(ModeratorPlayQueueEntryView entry)
    {
        return entry.Public.Fields.Count == 0
            ? "No entry details"
            : string.Join(
                " · ",
                entry.Public.Fields.Select(value => $"{value.Label}: {value.Value}")
            );
    }

    private sealed class EntryDraft
    {
        public string Priority { get; set; } = "0";
        public string PrivateNote { get; set; } = string.Empty;

        public static EntryDraft From(ModeratorPlayQueueEntryView entry)
        {
            return new EntryDraft
            {
                Priority = entry.Priority.ToString(CultureInfo.InvariantCulture),
                PrivateNote = entry.PrivateModeratorNote,
            };
        }
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
        public bool Required { get; set; }
        public string Choices { get; set; } = string.Empty;

        public PlayQueueFieldCommand ToCommand()
        {
            return new(
                Key,
                Label,
                Required,
                Choices.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            );
        }

        public static FieldDraft New()
        {
            return new() { Key = "field", Label = "Field" };
        }

        public static FieldDraft Standard(string key, string label)
        {
            return new() { Key = key, Label = label };
        }

        public static FieldDraft From(PlayQueueFieldView field)
        {
            return new()
            {
                Key = field.Key,
                Label = field.Label,
                Required = field.IsRequired,
                Choices = string.Join(", ", field.Choices),
            };
        }
    }
}
