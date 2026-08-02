using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PlayWithViewers;

public sealed class PlayQueueService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider,
    PlayQueueChangeNotifier? changes = null
) : IPlayQueueProjectionReader
{
    private const int _eventSchemaVersion = 1;
    private const int _maximumEventPayloadLength = 1024;
    private const int _gateCount = 64;
    private readonly SemaphoreSlim[] _mutationGates = CreateGates();
    private readonly PlayQueueChangeNotifier _changes = changes ?? new();

    public async Task<PlayQueueResult<PlayQueueSummary>> ConfigureAsync(
        int hostId,
        ConfigurePlayQueueCommand command,
        CancellationToken ct
    )
    {
        var rejection = ValidateConfiguration(hostId, command);
        if (rejection is not null)
        {
            return Rejected<PlayQueueSummary>(rejection);
        }

        var slug = PlayQueueInput.NormalizeSlug(command.Slug);
        return await MutateAsync<PlayQueueSummary>(
            hostId,
            slug,
            async (db, now) =>
            {
                if (!await db.Hosts.AnyAsync(host => host.Id == hostId, ct))
                {
                    return Rejected<PlayQueueSummary>(
                        new PlayQueueRejection.NotFound("The selected host does not exist.")
                    );
                }

                var queue = await db
                    .PlayQueues.Include(value => value.Fields)
                    .Include(value => value.RoleRequirements)
                    .SingleOrDefaultAsync(
                        value => value.HostId == hostId && value.Slug == slug,
                        ct
                    );
                if (queue is null)
                {
                    queue = new PlayQueue
                    {
                        HostId = hostId,
                        Slug = slug,
                        CreatedAtUtc = now,
                    };
                    db.PlayQueues.Add(queue);
                }
                else if (
                    await db.PlayQueueEntries.AnyAsync(value => value.QueueId == queue.Id, ct)
                    && !FieldShapeMatches(queue.Fields, command.Fields)
                )
                {
                    return Rejected<PlayQueueSummary>(
                        new PlayQueueRejection.Conflict(
                            "Entry fields cannot be replaced after viewers have joined."
                        )
                    );
                }

                queue.Name = command.Name.Trim();
                queue.ActivityName = command.ActivityName.Trim();
                queue.Capacity = command.Capacity;
                queue.IsOpen = command.IsOpen;
                queue.SelectionMode = command.SelectionMode;
                queue.ShowParticipantNames = command.ShowParticipantNames;
                queue.ReadinessTimeoutSeconds = command.ReadinessTimeoutSeconds;
                queue.HistoryRetentionDays = command.HistoryRetentionDays;
                queue.SkipExclusionMinutes = command.SkipExclusionMinutes;
                queue.UpdatedAtUtc = now;

                if (!FieldShapeMatches(queue.Fields, command.Fields))
                {
                    db.PlayQueueFields.RemoveRange(queue.Fields);
                    queue.Fields.Clear();
                    queue.Fields.AddRange(
                        command.Fields.Select(
                            (field, position) =>
                                new PlayQueueField
                                {
                                    Position = position,
                                    Key = PlayQueueInput.NormalizeKey(field.Key),
                                    Label = field.Label.Trim(),
                                    Choices = string.Join('\n', field.Choices ?? []),
                                }
                        )
                    );
                }

                db.PlayQueueRoleRequirements.RemoveRange(queue.RoleRequirements);
                queue.RoleRequirements.Clear();
                queue.RoleRequirements.AddRange(
                    command.RoleRequirements.Select(requirement => new PlayQueueRoleRequirement
                    {
                        Role = requirement.Role.Trim(),
                        MinimumCount = requirement.MinimumCount,
                    })
                );

                await db.SaveChangesAsync(ct);
                AddEvent(
                    db,
                    queue,
                    null,
                    PlayQueueEventKind.QueueConfigured,
                    new
                    {
                        queue.Slug,
                        queue.IsOpen,
                        queue.Capacity,
                        SelectionMode = queue.SelectionMode.ToString(),
                    },
                    now
                );
                await db.SaveChangesAsync(ct);
                return Succeeded(await LoadSummaryAsync(db, queue, ct));
            },
            ct,
            (db, now) => ConvergeQueueAsync(db, hostId, slug, now, ct)
        );
    }

    public async Task<IReadOnlyList<PlayQueueSummary>> GetQueuesForHostAsync(
        int hostId,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var queues = await db
            .PlayQueues.AsNoTracking()
            .Include(value => value.Fields)
            .Include(value => value.RoleRequirements)
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.Id)
            .ToListAsync(ct);
        var hostLogin = await db
            .Hosts.Where(host => host.Id == hostId)
            .Select(host => host.Login)
            .SingleOrDefaultAsync(ct);
        return queues.Select(queue => ToSummary(queue, hostLogin ?? string.Empty)).ToArray();
    }

    public Task<PlayQueueResult<PublicPlayQueueEntryView>> JoinAsync(
        int hostId,
        string queueSlug,
        JoinPlayQueueCommand command,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.Viewer.TwitchUserId))
        {
            return Task.FromResult<PlayQueueResult<PublicPlayQueueEntryView>>(
                Rejected<PublicPlayQueueEntryView>(
                    new PlayQueueRejection.Invalid("Sign in with Twitch to join this queue.")
                )
            );
        }

        var login = PlayQueueInput.NormalizeLogin(command.Viewer.Login);
        if (!PlayQueueInput.IsValidLogin(login))
        {
            return Task.FromResult<PlayQueueResult<PublicPlayQueueEntryView>>(
                Rejected<PublicPlayQueueEntryView>(
                    new PlayQueueRejection.Invalid("A valid Twitch login is required.")
                )
            );
        }

        if (command.Priority is < -1000 or > 1000)
        {
            return Task.FromResult<PlayQueueResult<PublicPlayQueueEntryView>>(
                Rejected<PublicPlayQueueEntryView>(
                    new PlayQueueRejection.Invalid("Priority must be from -1000 to 1000.")
                )
            );
        }

        var slug = PlayQueueInput.NormalizeSlug(queueSlug);
        return MutateAsync<PublicPlayQueueEntryView>(
            hostId,
            slug,
            async (db, now) =>
            {
                var queue = await LoadQueueAsync(db, hostId, slug, ct);
                if (queue is null)
                {
                    return Rejected<PublicPlayQueueEntryView>(
                        new PlayQueueRejection.NotFound("Queue not found.")
                    );
                }

                await ConvergeAndPruneAsync(db, queue, now, ct);
                if (!queue.IsOpen)
                {
                    return Rejected<PublicPlayQueueEntryView>(new PlayQueueRejection.Closed());
                }

                var values = ValidateValues(queue, command.FieldValues);
                if (values.Rejection is not null)
                {
                    return Rejected<PublicPlayQueueEntryView>(values.Rejection);
                }

                var identityKey = PlayQueueInput.IdentityKey(command.Viewer);
                var entry = await ReconcileEntryAsync(
                    db,
                    queue,
                    command.Viewer,
                    identityKey,
                    login,
                    ct
                );
                var exclusion = await db
                    .PlayQueueExclusions.Where(value =>
                        value.QueueId == queue.Id
                        && value.ExpiresAtUtc > now
                        && (
                            value.IdentityKey == identityKey
                            || value.IdentityKey == $"login:{login}"
                        )
                    )
                    .OrderByDescending(value => value.ExpiresAtUtc)
                    .FirstOrDefaultAsync(ct);
                if (exclusion is not null)
                {
                    return Rejected<PublicPlayQueueEntryView>(
                        new PlayQueueRejection.Excluded(exclusion.ExpiresAtUtc)
                    );
                }

                if (
                    entry is not null
                    && entry.Status
                        is PlayQueueEntryStatus.Waiting
                            or PlayQueueEntryStatus.AwaitingReady
                            or PlayQueueEntryStatus.Ready
                            or PlayQueueEntryStatus.Selected
                )
                {
                    return new PlayQueueResult<PublicPlayQueueEntryView>.Succeeded(
                        await ToPublicViewAsync(db, queue, entry, ct),
                        true
                    );
                }

                if (entry is null)
                {
                    entry = new PlayQueueEntry
                    {
                        HostId = hostId,
                        QueueId = queue.Id,
                        IdentityKey = identityKey,
                    };
                    db.PlayQueueEntries.Add(entry);
                }

                entry.TwitchUserId = CleanOptional(command.Viewer.TwitchUserId);
                entry.NormalizedLogin = login;
                entry.DisplayName = string.IsNullOrWhiteSpace(command.Viewer.DisplayName)
                    ? command.Viewer.Login.Trim()
                    : command.Viewer.DisplayName.Trim();
                entry.Priority = command.Priority;
                entry.Status = PlayQueueEntryStatus.Waiting;
                entry.JoinedAtUtc = now;
                entry.UpdatedAtUtc = now;
                entry.ReadyExpiresAtUtc = null;
                entry.PartyNumber = null;
                db.PlayQueueEntryValues.RemoveRange(entry.Values);
                entry.Values.Clear();
                foreach (var (field, value) in values.Values)
                {
                    entry.Values.Add(new PlayQueueEntryValue { FieldId = field.Id, Value = value });
                }

                await db.SaveChangesAsync(ct);
                AddEvent(db, queue, entry, PlayQueueEventKind.Joined, new { entry.Id }, now);
                await db.SaveChangesAsync(ct);
                return Succeeded(await ToPublicViewAsync(db, queue, entry, ct));
            },
            ct,
            (db, now) => ConvergeQueueAsync(db, hostId, slug, now, ct)
        );
    }

    public Task<PlayQueueResult<PublicPlayQueueEntryView>> LeaveAsync(
        int hostId,
        string queueSlug,
        PlayQueueViewerIdentity viewer,
        CancellationToken ct
    ) =>
        MutateViewerAsync(
            hostId,
            queueSlug,
            viewer,
            async (db, queue, entry, now) =>
            {
                if (entry.Status is PlayQueueEntryStatus.Left or PlayQueueEntryStatus.NoShow)
                {
                    return new PlayQueueResult<PublicPlayQueueEntryView>.Succeeded(
                        await ToPublicViewAsync(db, queue, entry, ct),
                        true
                    );
                }

                entry.Status = PlayQueueEntryStatus.Left;
                entry.ReadyExpiresAtUtc = null;
                entry.PartyNumber = null;
                entry.UpdatedAtUtc = now;
                AddEvent(db, queue, entry, PlayQueueEventKind.Left, new { entry.Id }, now);
                await db.SaveChangesAsync(ct);
                return Succeeded(await ToPublicViewAsync(db, queue, entry, ct));
            },
            ct
        );

    public Task<PlayQueueResult<PublicPlayQueueEntryView>> ReadyAsync(
        int hostId,
        string queueSlug,
        PlayQueueViewerIdentity viewer,
        CancellationToken ct
    ) =>
        MutateViewerAsync(
            hostId,
            queueSlug,
            viewer,
            async (db, queue, entry, now) =>
            {
                if (entry.Status == PlayQueueEntryStatus.Ready)
                {
                    return new PlayQueueResult<PublicPlayQueueEntryView>.Succeeded(
                        await ToPublicViewAsync(db, queue, entry, ct),
                        true
                    );
                }

                if (
                    entry.Status != PlayQueueEntryStatus.AwaitingReady
                    || entry.ReadyExpiresAtUtc is null
                    || entry.ReadyExpiresAtUtc <= now
                )
                {
                    return Rejected<PublicPlayQueueEntryView>(
                        new PlayQueueRejection.Conflict("There is no active ready check for you.")
                    );
                }

                entry.Status = PlayQueueEntryStatus.Ready;
                entry.ReadyExpiresAtUtc = null;
                entry.UpdatedAtUtc = now;
                AddEvent(db, queue, entry, PlayQueueEventKind.Ready, new { entry.Id }, now);
                await db.SaveChangesAsync(ct);
                return Succeeded(await ToPublicViewAsync(db, queue, entry, ct));
            },
            ct
        );

    public Task<PlayQueueResult<ModeratorPlayQueueEntryView>> StartReadyCheckAsync(
        int hostId,
        long entryId,
        CancellationToken ct
    ) =>
        MutateModeratorEntryAsync<ModeratorPlayQueueEntryView>(
            hostId,
            entryId,
            async (db, queue, entry, now) =>
            {
                if (
                    entry.Status is not (PlayQueueEntryStatus.Waiting or PlayQueueEntryStatus.Ready)
                )
                {
                    return Rejected<ModeratorPlayQueueEntryView>(
                        new PlayQueueRejection.Conflict("Only waiting viewers can be checked.")
                    );
                }

                entry.Status = PlayQueueEntryStatus.AwaitingReady;
                entry.ReadyExpiresAtUtc = now.AddSeconds(queue.ReadinessTimeoutSeconds);
                entry.UpdatedAtUtc = now;
                AddEvent(
                    db,
                    queue,
                    entry,
                    PlayQueueEventKind.ReadyCheckStarted,
                    new { entry.Id, entry.ReadyExpiresAtUtc },
                    now
                );
                await db.SaveChangesAsync(ct);
                return Succeeded(await ToModeratorViewAsync(db, queue, entry, ct));
            },
            ct
        );

    public Task<PlayQueueResult<ModeratorPlayQueueEntryView>> MarkNoShowAsync(
        int hostId,
        long entryId,
        CancellationToken ct
    ) =>
        SetModeratorOutcomeAsync(
            hostId,
            entryId,
            PlayQueueEntryStatus.NoShow,
            PlayQueueEventKind.NoShow,
            "No-show",
            ct
        );

    public Task<PlayQueueResult<ModeratorPlayQueueEntryView>> SkipAsync(
        int hostId,
        long entryId,
        CancellationToken ct
    ) =>
        SetModeratorOutcomeAsync(
            hostId,
            entryId,
            PlayQueueEntryStatus.Skipped,
            PlayQueueEventKind.Skipped,
            "Skipped by moderator",
            ct
        );

    public Task<PlayQueueResult<ModeratorPlayQueueEntryView>> UpdateEntryAsync(
        int hostId,
        long entryId,
        int priority,
        string privateModeratorNote,
        CancellationToken ct
    )
    {
        if (priority is < -1000 or > 1000 || privateModeratorNote.Trim().Length > 1000)
        {
            return Task.FromResult<PlayQueueResult<ModeratorPlayQueueEntryView>>(
                Rejected<ModeratorPlayQueueEntryView>(
                    new PlayQueueRejection.Invalid(
                        "Priority must be from -1000 to 1000 and the private note at most 1000 characters."
                    )
                )
            );
        }

        return MutateModeratorEntryAsync<ModeratorPlayQueueEntryView>(
            hostId,
            entryId,
            async (db, queue, entry, now) =>
            {
                var idempotent =
                    entry.Priority == priority
                    && entry.PrivateModeratorNote == privateModeratorNote.Trim();
                entry.Priority = priority;
                entry.PrivateModeratorNote = privateModeratorNote.Trim();
                entry.UpdatedAtUtc = now;
                await db.SaveChangesAsync(ct);
                return new PlayQueueResult<ModeratorPlayQueueEntryView>.Succeeded(
                    await ToModeratorViewAsync(db, queue, entry, ct),
                    idempotent
                );
            },
            ct
        );
    }

    public Task<PlayQueueResult<PlayQueueSelection>> ReplaceOneAsync(
        int hostId,
        long entryId,
        CancellationToken ct
    ) =>
        MutateModeratorEntryAsync<PlayQueueSelection>(
            hostId,
            entryId,
            async (db, queue, entry, now) =>
            {
                if (entry.Status != PlayQueueEntryStatus.Selected)
                {
                    return Rejected<PlayQueueSelection>(
                        new PlayQueueRejection.Conflict(
                            "Only a current party member can be replaced."
                        )
                    );
                }

                entry.Status = PlayQueueEntryStatus.Skipped;
                entry.PartyNumber = null;
                entry.UpdatedAtUtc = now;
                db.PlayQueueExclusions.Add(
                    new PlayQueueExclusion
                    {
                        HostId = hostId,
                        QueueId = queue.Id,
                        IdentityKey = entry.IdentityKey,
                        ExpiresAtUtc = now.AddMinutes(queue.SkipExclusionMinutes),
                        PrivateReason = "Replaced by moderator",
                    }
                );
                AddEvent(db, queue, entry, PlayQueueEventKind.Skipped, new { entry.Id }, now);

                var kept = queue
                    .Entries.Where(value => value.Status == PlayQueueEntryStatus.Selected)
                    .OrderBy(value => value.Id)
                    .ToList();
                var exclusions = await db
                    .PlayQueueExclusions.Where(value =>
                        value.QueueId == queue.Id && value.ExpiresAtUtc > now
                    )
                    .Select(value => value.IdentityKey)
                    .ToListAsync(ct);
                var history = await LatestParticipationAsync(db, queue.Id, ct);
                var candidates = OrderCandidates(
                        queue,
                        queue.Entries.Where(value =>
                            value.Status
                                is PlayQueueEntryStatus.Waiting
                                    or PlayQueueEntryStatus.Ready
                            && !exclusions.Contains(value.IdentityKey)
                        ),
                        history
                    )
                    .ToList();
                var replacements = SelectWithRoles(queue, candidates, kept);
                if (replacements.Rejection is not null)
                {
                    return Rejected<PlayQueueSelection>(replacements.Rejection);
                }

                foreach (var replacement in replacements.Members)
                {
                    replacement.Status = PlayQueueEntryStatus.Selected;
                    replacement.PartyNumber = queue.CurrentPartyNumber;
                    replacement.UpdatedAtUtc = now;
                    db.PlayQueueParticipation.Add(
                        new PlayQueueParticipation
                        {
                            HostId = hostId,
                            QueueId = queue.Id,
                            IdentityKey = replacement.IdentityKey,
                            ParticipatedAtUtc = now,
                        }
                    );
                }

                var party = kept.Concat(replacements.Members).ToList();
                AddEvent(
                    db,
                    queue,
                    null,
                    PlayQueueEventKind.PartySelected,
                    new
                    {
                        PartyNumber = queue.CurrentPartyNumber,
                        EntryIds = party.Select(value => value.Id).ToArray(),
                    },
                    now
                );
                await db.SaveChangesAsync(ct);
                return Succeeded(
                    new PlayQueueSelection(
                        queue.CurrentPartyNumber,
                        await ToModeratorViewsAsync(db, queue, party, ct)
                    )
                );
            },
            ct
        );

    public Task<PlayQueueResult<PlayQueueSummary>> SetOpenAsync(
        int hostId,
        string queueSlug,
        bool isOpen,
        CancellationToken ct
    ) =>
        MutateAsync<PlayQueueSummary>(
            hostId,
            PlayQueueInput.NormalizeSlug(queueSlug),
            async (db, now) =>
            {
                var queue = await LoadQueueAsync(
                    db,
                    hostId,
                    PlayQueueInput.NormalizeSlug(queueSlug),
                    ct
                );
                if (queue is null)
                {
                    return Rejected<PlayQueueSummary>(
                        new PlayQueueRejection.NotFound("Queue not found.")
                    );
                }

                var idempotent = queue.IsOpen == isOpen;
                queue.IsOpen = isOpen;
                queue.UpdatedAtUtc = now;
                if (!idempotent)
                {
                    AddEvent(
                        db,
                        queue,
                        null,
                        isOpen
                            ? PlayQueueEventKind.QueueConfigured
                            : PlayQueueEventKind.QueueClosed,
                        new { queue.Slug, queue.IsOpen },
                        now
                    );
                    await db.SaveChangesAsync(ct);
                }

                return new PlayQueueResult<PlayQueueSummary>.Succeeded(
                    await LoadSummaryAsync(db, queue, ct),
                    idempotent
                );
            },
            ct,
            (db, now) =>
                ConvergeQueueAsync(db, hostId, PlayQueueInput.NormalizeSlug(queueSlug), now, ct)
        );

    public Task<PlayQueueResult<PlayQueueSelection>> SelectPartyAsync(
        int hostId,
        string queueSlug,
        bool keepCurrentParty,
        CancellationToken ct
    ) =>
        MutateAsync<PlayQueueSelection>(
            hostId,
            PlayQueueInput.NormalizeSlug(queueSlug),
            async (db, now) =>
            {
                var queue = await LoadQueueAsync(
                    db,
                    hostId,
                    PlayQueueInput.NormalizeSlug(queueSlug),
                    ct
                );
                if (queue is null)
                {
                    return Rejected<PlayQueueSelection>(
                        new PlayQueueRejection.NotFound("Queue not found.")
                    );
                }

                await ConvergeAndPruneAsync(db, queue, now, ct);
                var current = queue
                    .Entries.Where(value => value.Status == PlayQueueEntryStatus.Selected)
                    .ToList();
                if (keepCurrentParty && current.Count > 0)
                {
                    return new PlayQueueResult<PlayQueueSelection>.Succeeded(
                        new PlayQueueSelection(
                            queue.CurrentPartyNumber,
                            await ToModeratorViewsAsync(db, queue, current, ct)
                        ),
                        true
                    );
                }

                foreach (var member in current)
                {
                    member.Status = PlayQueueEntryStatus.Waiting;
                    member.JoinedAtUtc = now;
                    member.PartyNumber = null;
                    member.UpdatedAtUtc = now;
                }

                var candidates = queue
                    .Entries.Where(value =>
                        value.Status is PlayQueueEntryStatus.Waiting or PlayQueueEntryStatus.Ready
                    )
                    .ToList();
                var excludedKeys = await db
                    .PlayQueueExclusions.Where(value =>
                        value.QueueId == queue.Id && value.ExpiresAtUtc > now
                    )
                    .Select(value => value.IdentityKey)
                    .ToListAsync(ct);
                candidates.RemoveAll(value => excludedKeys.Contains(value.IdentityKey));
                var lastParticipation = await LatestParticipationAsync(db, queue.Id, ct);
                var ordered = OrderCandidates(queue, candidates, lastParticipation).ToList();
                var selected = SelectWithRoles(queue, ordered, []);
                if (selected.Rejection is not null)
                {
                    return Rejected<PlayQueueSelection>(selected.Rejection);
                }

                queue.CurrentPartyNumber++;
                queue.UpdatedAtUtc = now;
                foreach (var member in selected.Members)
                {
                    member.Status = PlayQueueEntryStatus.Selected;
                    member.ReadyExpiresAtUtc = null;
                    member.PartyNumber = queue.CurrentPartyNumber;
                    member.UpdatedAtUtc = now;
                    db.PlayQueueParticipation.Add(
                        new PlayQueueParticipation
                        {
                            HostId = hostId,
                            QueueId = queue.Id,
                            IdentityKey = member.IdentityKey,
                            ParticipatedAtUtc = now,
                        }
                    );
                }

                AddEvent(
                    db,
                    queue,
                    null,
                    PlayQueueEventKind.PartySelected,
                    new
                    {
                        PartyNumber = queue.CurrentPartyNumber,
                        EntryIds = selected.Members.Select(value => value.Id).ToArray(),
                    },
                    now
                );
                await db.SaveChangesAsync(ct);
                return Succeeded(
                    new PlayQueueSelection(
                        queue.CurrentPartyNumber,
                        await ToModeratorViewsAsync(db, queue, selected.Members, ct)
                    )
                );
            },
            ct,
            (db, now) =>
                ConvergeQueueAsync(db, hostId, PlayQueueInput.NormalizeSlug(queueSlug), now, ct)
        );

    public async Task<PublicPlayQueueSnapshot?> GetPublicPageAsync(
        string hostLogin,
        string queueSlug,
        CancellationToken ct
    )
    {
        if (
            !await HostFeatureAvailability.IsEnabledAsync(
                dbFactory,
                hostLogin,
                HostFeatureFlags.PlayWithViewers,
                ct
            )
        )
        {
            return null;
        }

        var normalizedHost = PlayQueueInput.NormalizeLogin(hostLogin);
        await using var lookup = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await lookup
            .Hosts.Where(host => host.Login == normalizedHost)
            .Select(host => (int?)host.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is null)
        {
            return null;
        }

        return await ReadPageAsync(
            hostId.Value,
            PlayQueueInput.NormalizeSlug(queueSlug),
            async (db, queue) =>
            {
                var waiting = OrderedWaiting(queue.Entries)
                    .Select((entry, index) => ToPublicView(queue, entry, index + 1))
                    .ToArray();
                var party = queue
                    .Entries.Where(value => value.Status == PlayQueueEntryStatus.Selected)
                    .OrderBy(value => value.Id)
                    .Select(entry => ToPublicView(queue, entry, 0))
                    .ToArray();
                return new PublicPlayQueueSnapshot(
                    await LoadSummaryAsync(db, queue, ct),
                    waiting,
                    party
                );
            },
            ct
        );
    }

    public Task<ModeratorPlayQueuePage?> GetModeratorPageAsync(
        int hostId,
        string queueSlug,
        CancellationToken ct
    ) =>
        ReadPageAsync(
            hostId,
            PlayQueueInput.NormalizeSlug(queueSlug),
            async (db, queue) =>
            {
                var waitingEntries = OrderedWaiting(queue.Entries).ToList();
                var waiting = await ToModeratorViewsAsync(db, queue, waitingEntries, ct);
                var party = await ToModeratorViewsAsync(
                    db,
                    queue,
                    queue
                        .Entries.Where(value => value.Status == PlayQueueEntryStatus.Selected)
                        .OrderBy(value => value.Id)
                        .ToList(),
                    ct
                );
                var history = await LatestParticipationAsync(db, queue.Id, ct);
                var exclusions = await db
                    .PlayQueueExclusions.Where(value =>
                        value.QueueId == queue.Id
                        && value.ExpiresAtUtc > timeProvider.GetUtcNow().UtcDateTime
                    )
                    .Select(value => value.IdentityKey)
                    .ToListAsync(ct);
                var next = OrderCandidates(
                        queue,
                        waitingEntries.Where(value => !exclusions.Contains(value.IdentityKey)),
                        history
                    )
                    .Take(queue.Capacity)
                    .ToList();
                return new ModeratorPlayQueuePage(
                    await LoadSummaryAsync(db, queue, ct),
                    waiting,
                    party,
                    await ToModeratorViewsAsync(db, queue, next, ct)
                );
            },
            ct
        );

    public Task<PlayQueueOverlayState?> ReadOverlayStateAsync(
        int hostId,
        int queueId,
        int currentLimit,
        int nextLimit,
        CancellationToken cancellationToken
    )
    {
        if (currentLimit is < 0 or > 12 || nextLimit is < 0 or > 12 || queueId <= 0)
        {
            return Task.FromResult<PlayQueueOverlayState?>(null);
        }

        return ReadOverlayStateCoreAsync(
            hostId,
            queueId,
            currentLimit,
            nextLimit,
            cancellationToken
        );
    }

    public async Task<PlayQueueResult<PublicPlayQueueEntryView>> GetPositionAsync(
        int hostId,
        string queueSlug,
        PlayQueueViewerIdentity viewer,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<PublicPlayQueueEntryView>(new PlayQueueRejection.FeatureDisabled());
        }

        var page = await GetModeratorPageAsync(hostId, queueSlug, ct);
        if (page is null)
        {
            return Rejected<PublicPlayQueueEntryView>(
                new PlayQueueRejection.NotFound("Queue not found.")
            );
        }

        var identity = PlayQueueInput.IdentityKey(viewer);
        var login = PlayQueueInput.NormalizeLogin(viewer.Login);
        var entry = page
            .Waiting.Concat(page.CurrentParty)
            .FirstOrDefault(value =>
                value.TwitchUserId is not null && identity == $"id:{value.TwitchUserId}"
                || value.NormalizedLogin == login
            );
        return entry is null
            ? Rejected<PublicPlayQueueEntryView>(new PlayQueueRejection.NotJoined())
            : Succeeded(entry.Public);
    }

    public async Task<IReadOnlyList<PlayQueueEventView>> GetEventsAsync(
        int hostId,
        long afterId,
        int count,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return [];
        }

        var take = Math.Clamp(count, 1, PlayQueueLimits.MaximumEventReadCount);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .PlayQueueEvents.AsNoTracking()
            .Where(value => value.HostId == hostId && value.Id > afterId)
            .OrderBy(value => value.Id)
            .Take(take)
            .Select(value => new PlayQueueEventView(
                value.Id,
                value.HostId,
                value.QueueId,
                value.EntryId,
                value.SchemaVersion,
                value.Kind,
                value.PublicPayload,
                value.OccurredAtUtc
            ))
            .ToListAsync(ct);
    }

    private Task<PlayQueueResult<ModeratorPlayQueueEntryView>> SetModeratorOutcomeAsync(
        int hostId,
        long entryId,
        PlayQueueEntryStatus status,
        PlayQueueEventKind eventKind,
        string reason,
        CancellationToken ct
    ) =>
        MutateModeratorEntryAsync<ModeratorPlayQueueEntryView>(
            hostId,
            entryId,
            async (db, queue, entry, now) =>
            {
                var idempotent = entry.Status == status;
                entry.Status = status;
                entry.ReadyExpiresAtUtc = null;
                entry.PartyNumber = null;
                entry.UpdatedAtUtc = now;
                if (!idempotent)
                {
                    db.PlayQueueExclusions.Add(
                        new PlayQueueExclusion
                        {
                            HostId = hostId,
                            QueueId = queue.Id,
                            IdentityKey = entry.IdentityKey,
                            ExpiresAtUtc = now.AddMinutes(queue.SkipExclusionMinutes),
                            PrivateReason = reason,
                        }
                    );
                    AddEvent(db, queue, entry, eventKind, new { entry.Id }, now);
                    await db.SaveChangesAsync(ct);
                }

                return new PlayQueueResult<ModeratorPlayQueueEntryView>.Succeeded(
                    await ToModeratorViewAsync(db, queue, entry, ct),
                    idempotent
                );
            },
            ct
        );

    private Task<PlayQueueResult<T>> MutateModeratorEntryAsync<T>(
        int hostId,
        long entryId,
        Func<
            BlokeBotDbContext,
            PlayQueue,
            PlayQueueEntry,
            DateTime,
            Task<PlayQueueResult<T>>
        > mutate,
        CancellationToken ct
    ) =>
        MutateAsync(
            hostId,
            $"entry-{entryId}",
            async (db, now) =>
            {
                var entry = await db
                    .PlayQueueEntries.Include(value => value.Values)
                        .ThenInclude(value => value.Field)
                    .SingleOrDefaultAsync(
                        value => value.HostId == hostId && value.Id == entryId,
                        ct
                    );
                if (entry is null)
                {
                    return Rejected<T>(new PlayQueueRejection.NotFound("Queue entry not found."));
                }

                var queue = await LoadQueueAsync(db, hostId, entry.QueueId, ct);
                if (queue is null)
                {
                    return Rejected<T>(new PlayQueueRejection.NotFound("Queue not found."));
                }

                await ConvergeAndPruneAsync(db, queue, now, ct);
                return await mutate(db, queue, entry, now);
            },
            ct,
            (db, now) => ConvergeEntryQueueAsync(db, hostId, entryId, now, ct)
        );

    private Task<PlayQueueResult<PublicPlayQueueEntryView>> MutateViewerAsync(
        int hostId,
        string queueSlug,
        PlayQueueViewerIdentity viewer,
        Func<
            BlokeBotDbContext,
            PlayQueue,
            PlayQueueEntry,
            DateTime,
            Task<PlayQueueResult<PublicPlayQueueEntryView>>
        > mutate,
        CancellationToken ct
    )
    {
        var slug = PlayQueueInput.NormalizeSlug(queueSlug);
        return MutateAsync(
            hostId,
            slug,
            async (db, now) =>
            {
                var queue = await LoadQueueAsync(db, hostId, slug, ct);
                if (queue is null)
                {
                    return Rejected<PublicPlayQueueEntryView>(
                        new PlayQueueRejection.NotFound("Queue not found.")
                    );
                }

                await ConvergeAndPruneAsync(db, queue, now, ct);
                var entry = await FindEntryAsync(db, queue.Id, viewer, ct);
                return entry is null
                    ? Rejected<PublicPlayQueueEntryView>(new PlayQueueRejection.NotJoined())
                    : await mutate(db, queue, entry, now);
            },
            ct,
            (db, now) => ConvergeQueueAsync(db, hostId, slug, now, ct)
        );
    }

    private async Task<T?> ReadPageAsync<T>(
        int hostId,
        string slug,
        Func<BlokeBotDbContext, PlayQueue, Task<T>> project,
        CancellationToken ct
    )
        where T : class
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return null;
        }

        var gate = GateFor(hostId, slug);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var queue = await LoadQueueAsync(db, hostId, slug, ct);
            if (queue is null)
            {
                return null;
            }

            var changed = await ConvergeAndPruneAsync(
                db,
                queue,
                timeProvider.GetUtcNow().UtcDateTime,
                ct
            );
            if (changed)
            {
                await db.SaveChangesAsync(ct);
            }

            var value = await project(db, queue);
            var committedChanges = CommittedChanges(db, hostId);
            await transaction.CommitAsync(ct);
            if (changed)
            {
                await events.PublishAsync(AppEventKind.PlayQueuesChanged, ct);
                await NotifyOverlayChangesAsync(committedChanges, ct);
            }

            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PlayQueueResult<T>> MutateAsync<T>(
        int hostId,
        string slug,
        Func<BlokeBotDbContext, DateTime, Task<PlayQueueResult<T>>> mutate,
        CancellationToken ct,
        Func<BlokeBotDbContext, DateTime, Task<bool>>? convergeBeforeMutation = null
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<T>(new PlayQueueRejection.FeatureDisabled());
        }

        var gate = GateFor(hostId, slug);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var converged =
                convergeBeforeMutation is not null && await convergeBeforeMutation(db, now);
            if (converged)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CreateSavepointAsync("AfterReadinessConvergence", ct);
            }

            var result = await mutate(db, now);
            if (result is PlayQueueResult<T>.Succeeded succeeded)
            {
                await db.SaveChangesAsync(ct);
                var committedChanges =
                    succeeded.WasIdempotent && !converged ? [] : CommittedChanges(db, hostId);
                await transaction.CommitAsync(ct);
                await events.PublishAsync(AppEventKind.PlayQueuesChanged, ct);
                await NotifyOverlayChangesAsync(committedChanges, ct);
            }
            else if (converged)
            {
                var committedChanges = CommittedChanges(db, hostId);
                await transaction.RollbackToSavepointAsync("AfterReadinessConvergence", ct);
                await transaction.CommitAsync(ct);
                await events.PublishAsync(AppEventKind.PlayQueuesChanged, ct);
                await NotifyOverlayChangesAsync(committedChanges, ct);
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private Task<bool> FeatureIsEnabledAsync(int hostId, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(
            dbFactory,
            hostId,
            HostFeatureFlags.PlayWithViewers,
            ct
        );

    private Task<bool> OverlayFeaturesAreEnabledAsync(int hostId, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(
            dbFactory,
            hostId,
            HostFeatureFlags.Overlays | HostFeatureFlags.PlayWithViewers,
            ct
        );

    private async Task<bool> ConvergeQueueAsync(
        BlokeBotDbContext db,
        int hostId,
        string slug,
        DateTime now,
        CancellationToken ct
    )
    {
        var queue = await LoadQueueAsync(db, hostId, slug, ct);
        return queue is not null && await ConvergeAndPruneAsync(db, queue, now, ct);
    }

    private async Task<bool> ConvergeEntryQueueAsync(
        BlokeBotDbContext db,
        int hostId,
        long entryId,
        DateTime now,
        CancellationToken ct
    )
    {
        var queueId = await db
            .PlayQueueEntries.Where(value => value.HostId == hostId && value.Id == entryId)
            .Select(value => (int?)value.QueueId)
            .SingleOrDefaultAsync(ct);
        if (queueId is null)
        {
            return false;
        }

        var queue = await LoadQueueAsync(db, hostId, queueId.Value, ct);
        return queue is not null && await ConvergeAndPruneAsync(db, queue, now, ct);
    }

    private async Task<bool> ConvergeAndPruneAsync(
        BlokeBotDbContext db,
        PlayQueue queue,
        DateTime now,
        CancellationToken ct
    )
    {
        var changed = false;
        foreach (
            var entry in queue.Entries.Where(value =>
                value.Status == PlayQueueEntryStatus.AwaitingReady && value.ReadyExpiresAtUtc <= now
            )
        )
        {
            entry.Status = PlayQueueEntryStatus.NoShow;
            entry.ReadyExpiresAtUtc = null;
            entry.UpdatedAtUtc = now;
            db.PlayQueueExclusions.Add(
                new PlayQueueExclusion
                {
                    HostId = queue.HostId,
                    QueueId = queue.Id,
                    IdentityKey = entry.IdentityKey,
                    ExpiresAtUtc = now.AddMinutes(queue.SkipExclusionMinutes),
                    PrivateReason = "Ready check expired",
                }
            );
            AddEvent(db, queue, entry, PlayQueueEventKind.NoShow, new { entry.Id }, now);
            changed = true;
        }

        var historyCutoff = now.AddDays(-queue.HistoryRetentionDays);
        var expiredHistory = await db
            .PlayQueueParticipation.Where(value =>
                value.QueueId == queue.Id && value.ParticipatedAtUtc < historyCutoff
            )
            .ToListAsync(ct);
        var expiredExclusions = await db
            .PlayQueueExclusions.Where(value =>
                value.QueueId == queue.Id && value.ExpiresAtUtc <= now
            )
            .ToListAsync(ct);
        if (expiredHistory.Count > 0 || expiredExclusions.Count > 0)
        {
            db.PlayQueueParticipation.RemoveRange(expiredHistory);
            db.PlayQueueExclusions.RemoveRange(expiredExclusions);
            changed = true;
        }

        return changed;
    }

    private static CandidateSelection SelectWithRoles(
        PlayQueue queue,
        IReadOnlyList<PlayQueueEntry> ordered,
        IReadOnlyList<PlayQueueEntry> kept
    )
    {
        var needed = queue.Capacity - kept.Count;
        if (needed < 0 || ordered.Count < needed)
        {
            return new CandidateSelection(
                [],
                new PlayQueueRejection.Composition(
                    $"This party needs {queue.Capacity} viewers but only {kept.Count + ordered.Count} are eligible."
                )
            );
        }

        var selected = new List<PlayQueueEntry>(needed);
        foreach (var requirement in queue.RoleRequirements.OrderBy(value => value.Role))
        {
            var keptCount = kept.Count(value =>
                string.Equals(
                    PreferredRole(value),
                    requirement.Role,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            var required = Math.Max(0, requirement.MinimumCount - keptCount);
            var matching = ordered
                .Where(value =>
                    !selected.Contains(value)
                    && string.Equals(
                        PreferredRole(value),
                        requirement.Role,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Take(required)
                .ToList();
            selected.AddRange(matching);
        }

        selected.AddRange(
            ordered.Where(value => !selected.Contains(value)).Take(needed - selected.Count)
        );
        return new CandidateSelection(selected, null);
    }

    private static IOrderedEnumerable<PlayQueueEntry> OrderCandidates(
        PlayQueue queue,
        IEnumerable<PlayQueueEntry> candidates,
        IReadOnlyDictionary<string, DateTime> history
    )
    {
        var ordered = candidates.OrderByDescending(value => value.Priority);
        return queue.SelectionMode switch
        {
            PlayQueueSelectionMode.JoinOrder => ordered
                .ThenBy(value => value.JoinedAtUtc)
                .ThenBy(value => value.Id),
            PlayQueueSelectionMode.LeastRecentParticipation => ordered
                .ThenBy(value => history.ContainsKey(value.IdentityKey) ? 1 : 0)
                .ThenBy(value =>
                    history.TryGetValue(value.IdentityKey, out var participated)
                        ? participated
                        : DateTime.MinValue
                )
                .ThenBy(value => value.JoinedAtUtc)
                .ThenBy(value => value.Id),
            _ => throw new InvalidOperationException("Unknown queue selection mode."),
        };
    }

    private static IOrderedEnumerable<PlayQueueEntry> OrderedWaiting(
        IEnumerable<PlayQueueEntry> entries
    ) =>
        entries
            .Where(value =>
                value.Status
                    is PlayQueueEntryStatus.Waiting
                        or PlayQueueEntryStatus.AwaitingReady
                        or PlayQueueEntryStatus.Ready
            )
            .OrderByDescending(value => value.Priority)
            .ThenBy(value => value.JoinedAtUtc)
            .ThenBy(value => value.Id);

    private async Task<PlayQueue?> LoadQueueAsync(
        BlokeBotDbContext db,
        int hostId,
        string slug,
        CancellationToken ct
    ) =>
        await db
            .PlayQueues.Include(value => value.Fields)
            .Include(value => value.RoleRequirements)
            .Include(value => value.Entries)
                .ThenInclude(value => value.Values)
                    .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(value => value.HostId == hostId && value.Slug == slug, ct);

    private async Task<PlayQueue?> LoadQueueAsync(
        BlokeBotDbContext db,
        int hostId,
        int queueId,
        CancellationToken ct
    ) =>
        await db
            .PlayQueues.Include(value => value.Fields)
            .Include(value => value.RoleRequirements)
            .Include(value => value.Entries)
                .ThenInclude(value => value.Values)
                    .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(value => value.HostId == hostId && value.Id == queueId, ct);

    private static async Task<PlayQueueEntry?> FindEntryAsync(
        BlokeBotDbContext db,
        int queueId,
        PlayQueueViewerIdentity viewer,
        CancellationToken ct
    )
    {
        var identity = PlayQueueInput.IdentityKey(viewer);
        var login = PlayQueueInput.NormalizeLogin(viewer.Login);
        return await db
            .PlayQueueEntries.Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .Where(value =>
                value.QueueId == queueId
                && (value.IdentityKey == identity || value.NormalizedLogin == login)
            )
            .OrderByDescending(value => value.IdentityKey == identity)
            .ThenBy(value => value.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<PlayQueueEntry?> ReconcileEntryAsync(
        BlokeBotDbContext db,
        PlayQueue queue,
        PlayQueueViewerIdentity viewer,
        string identityKey,
        string login,
        CancellationToken ct
    )
    {
        var byIdentity = await db
            .PlayQueueEntries.Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(
                value => value.QueueId == queue.Id && value.IdentityKey == identityKey,
                ct
            );
        if (byIdentity is not null)
        {
            byIdentity.NormalizedLogin = login;
            byIdentity.DisplayName = string.IsNullOrWhiteSpace(viewer.DisplayName)
                ? viewer.Login.Trim()
                : viewer.DisplayName.Trim();
            return byIdentity;
        }

        if (string.IsNullOrWhiteSpace(viewer.TwitchUserId))
        {
            return await db
                .PlayQueueEntries.Include(value => value.Values)
                    .ThenInclude(value => value.Field)
                .SingleOrDefaultAsync(
                    value => value.QueueId == queue.Id && value.NormalizedLogin == login,
                    ct
                );
        }

        var fallback = await db
            .PlayQueueEntries.Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .SingleOrDefaultAsync(
                value =>
                    value.QueueId == queue.Id
                    && value.IdentityKey == $"login:{login}"
                    && value.NormalizedLogin == login,
                ct
            );
        if (fallback is not null)
        {
            fallback.IdentityKey = identityKey;
            fallback.TwitchUserId = viewer.TwitchUserId.Trim();
        }

        return fallback;
    }

    private async Task<Dictionary<string, DateTime>> LatestParticipationAsync(
        BlokeBotDbContext db,
        int queueId,
        CancellationToken ct
    ) =>
        await db
            .PlayQueueParticipation.Where(value => value.QueueId == queueId)
            .GroupBy(value => value.IdentityKey)
            .ToDictionaryAsync(
                group => group.Key,
                group => group.Max(value => value.ParticipatedAtUtc),
                ct
            );

    private async Task<PlayQueueSummary> LoadSummaryAsync(
        BlokeBotDbContext db,
        PlayQueue queue,
        CancellationToken ct
    )
    {
        var hostLogin = await db
            .Hosts.Where(host => host.Id == queue.HostId)
            .Select(host => host.Login)
            .SingleAsync(ct);
        return ToSummary(queue, hostLogin);
    }

    private static PlayQueueSummary ToSummary(PlayQueue queue, string hostLogin) =>
        new(
            queue.Id,
            queue.HostId,
            hostLogin,
            queue.Slug,
            queue.Name,
            queue.ActivityName,
            queue.Capacity,
            queue.IsOpen,
            queue.SelectionMode,
            queue.ShowParticipantNames,
            queue.ReadinessTimeoutSeconds,
            queue.HistoryRetentionDays,
            queue.SkipExclusionMinutes,
            queue.SelectionMode == PlayQueueSelectionMode.JoinOrder
                ? "Higher priority, then earlier join time, then entry ID."
                : "Higher priority, then least recent participation, earlier join time, then entry ID.",
            queue
                .Fields.OrderBy(value => value.Position)
                .Select(value => new PlayQueueFieldView(
                    value.Id,
                    value.Key,
                    value.Label,
                    Choices(value.Choices)
                ))
                .ToArray(),
            queue
                .RoleRequirements.OrderBy(value => value.Role)
                .Select(value => new PlayQueueRoleRequirementView(value.Role, value.MinimumCount))
                .ToArray()
        );

    private async Task<PublicPlayQueueEntryView> ToPublicViewAsync(
        BlokeBotDbContext db,
        PlayQueue queue,
        PlayQueueEntry entry,
        CancellationToken ct
    )
    {
        var position =
            await db
                .PlayQueueEntries.Where(value =>
                    value.QueueId == queue.Id
                    && (
                        value.Status == PlayQueueEntryStatus.Waiting
                        || value.Status == PlayQueueEntryStatus.AwaitingReady
                        || value.Status == PlayQueueEntryStatus.Ready
                    )
                    && (
                        value.Priority > entry.Priority
                        || value.Priority == entry.Priority && value.JoinedAtUtc < entry.JoinedAtUtc
                        || value.Priority == entry.Priority
                            && value.JoinedAtUtc == entry.JoinedAtUtc
                            && value.Id < entry.Id
                    )
                )
                .LongCountAsync(ct) + 1;
        return ToPublicView(queue, entry, position);
    }

    private static PublicPlayQueueEntryView ToPublicView(
        PlayQueue queue,
        PlayQueueEntry entry,
        long position
    ) =>
        new(
            position,
            queue.ShowParticipantNames ? entry.DisplayName : null,
            entry.Status,
            PublicFields(queue, entry)
        )
        {
            InternalEntryId = entry.Id,
        };

    private async Task<ModeratorPlayQueueEntryView> ToModeratorViewAsync(
        BlokeBotDbContext db,
        PlayQueue queue,
        PlayQueueEntry entry,
        CancellationToken ct
    )
    {
        var history = await db
            .PlayQueueParticipation.Where(value =>
                value.QueueId == queue.Id && value.IdentityKey == entry.IdentityKey
            )
            .MaxAsync(value => (DateTime?)value.ParticipatedAtUtc, ct);
        var exclusion = await db
            .PlayQueueExclusions.Where(value =>
                value.QueueId == queue.Id
                && value.IdentityKey == entry.IdentityKey
                && value.ExpiresAtUtc > timeProvider.GetUtcNow().UtcDateTime
            )
            .MaxAsync(value => (DateTime?)value.ExpiresAtUtc, ct);
        var publicView = await ToPublicViewAsync(db, queue, entry, ct);
        return new ModeratorPlayQueueEntryView(
            entry.Id,
            publicView,
            entry.NormalizedLogin,
            entry.TwitchUserId,
            entry.Priority,
            entry.PrivateModeratorNote,
            entry.JoinedAtUtc,
            history,
            exclusion
        );
    }

    private async Task<IReadOnlyList<ModeratorPlayQueueEntryView>> ToModeratorViewsAsync(
        BlokeBotDbContext db,
        PlayQueue queue,
        IReadOnlyList<PlayQueueEntry> entries,
        CancellationToken ct
    )
    {
        var values = new List<ModeratorPlayQueueEntryView>(entries.Count);
        foreach (var entry in entries)
        {
            values.Add(await ToModeratorViewAsync(db, queue, entry, ct));
        }

        return values;
    }

    private static ConfigurationValues ValidateValues(
        PlayQueue queue,
        IReadOnlyDictionary<string, string> supplied
    )
    {
        var normalized = supplied.ToDictionary(
            pair => PlayQueueInput.NormalizeKey(pair.Key),
            pair => pair.Value.Trim(),
            StringComparer.Ordinal
        );
        var unknown = normalized
            .Keys.Except(queue.Fields.Select(field => field.Key))
            .FirstOrDefault();
        if (unknown is not null)
        {
            return new ConfigurationValues(
                [],
                new PlayQueueRejection.Invalid($"Unknown queue field '{unknown}'.")
            );
        }

        var values = new List<(PlayQueueField Field, string Value)>();
        foreach (var field in queue.Fields.OrderBy(value => value.Position))
        {
            normalized.TryGetValue(field.Key, out var value);
            value ??= string.Empty;
            if (value.Length > 200)
            {
                return new ConfigurationValues(
                    [],
                    new PlayQueueRejection.Invalid($"{field.Label} must be 200 characters or less.")
                );
            }

            var choices = Choices(field.Choices);
            if (
                value.Length > 0
                && choices.Count > 0
                && !choices.Contains(value, StringComparer.OrdinalIgnoreCase)
            )
            {
                return new ConfigurationValues(
                    [],
                    new PlayQueueRejection.Invalid($"Choose a valid value for {field.Label}.")
                );
            }

            if (value.Length > 0)
            {
                values.Add(
                    (
                        field,
                        choices.FirstOrDefault(choice =>
                            choice.Equals(value, StringComparison.OrdinalIgnoreCase)
                        ) ?? value
                    )
                );
            }
        }

        return new ConfigurationValues(values, null);
    }

    private static PlayQueueRejection? ValidateConfiguration(
        int hostId,
        ConfigurePlayQueueCommand command
    )
    {
        var slug = PlayQueueInput.NormalizeSlug(command.Slug);
        if (hostId <= 0)
        {
            return new PlayQueueRejection.Invalid("A host is required.");
        }

        if (!PlayQueueInput.IsValidSlug(slug))
        {
            return new PlayQueueRejection.Invalid("Use a 1-48 character lowercase queue slug.");
        }

        if (
            command.Name.Trim().Length is < 1 or > 100
            || command.ActivityName.Trim().Length is < 1 or > 100
        )
        {
            return new PlayQueueRejection.Invalid(
                "Queue and activity names must be 1-100 characters."
            );
        }

        if (command.Capacity is < 1 or > PlayQueueLimits.MaximumCapacity)
        {
            return new PlayQueueRejection.Invalid("Capacity must be from 1 to 50.");
        }

        if (command.ReadinessTimeoutSeconds is < 15 or > 3600)
        {
            return new PlayQueueRejection.Invalid(
                "Readiness expiry must be from 15 to 3600 seconds."
            );
        }

        if (command.HistoryRetentionDays is < 1 or > 365)
        {
            return new PlayQueueRejection.Invalid("History retention must be from 1 to 365 days.");
        }

        if (command.SkipExclusionMinutes is < 1 or > 10080)
        {
            return new PlayQueueRejection.Invalid(
                "Skip exclusion must be from 1 minute to 7 days."
            );
        }

        if (command.Fields.Count > PlayQueueLimits.MaximumFields)
        {
            return new PlayQueueRejection.Invalid("A queue can have at most 12 fields.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in command.Fields)
        {
            var key = PlayQueueInput.NormalizeKey(field.Key);
            if (
                !PlayQueueInput.IsValidSlug(key)
                || !keys.Add(key)
                || field.Label.Trim().Length is < 1 or > 100
            )
            {
                return new PlayQueueRejection.Invalid(
                    "Queue fields need unique valid keys and labels."
                );
            }

            if (
                (field.Choices?.Count ?? 0) > 30
                || (field.Choices?.Any(value => value.Trim().Length is < 1 or > 100) ?? false)
            )
            {
                return new PlayQueueRejection.Invalid(
                    "Field choices must contain 1-100 characters and at most 30 choices."
                );
            }
        }

        if (command.RoleRequirements.Count > PlayQueueLimits.MaximumRoles)
        {
            return new PlayQueueRejection.Invalid("A queue can require at most 12 roles.");
        }

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (
            command.RoleRequirements.Any(value =>
                value.Role.Trim().Length is < 1 or > 64
                || value.MinimumCount is < 1
                || !roles.Add(value.Role.Trim())
            )
            || command.RoleRequirements.Sum(value => value.MinimumCount) > command.Capacity
        )
        {
            return new PlayQueueRejection.Invalid(
                "Role requirements must be unique and fit the party capacity."
            );
        }

        if (
            command.RoleRequirements.Count > 0
            && !command.Fields.Any(value =>
                PlayQueueInput.NormalizeKey(value.Key) == "preferred-role"
            )
        )
        {
            return new PlayQueueRejection.Invalid(
                "Role composition requires a preferred-role field."
            );
        }

        return null;
    }

    private static bool FieldShapeMatches(
        IReadOnlyCollection<PlayQueueField> stored,
        IReadOnlyList<PlayQueueFieldCommand> commanded
    ) =>
        stored.Count == commanded.Count
        && stored
            .OrderBy(value => value.Position)
            .Zip(commanded)
            .All(pair =>
                pair.First.Key == PlayQueueInput.NormalizeKey(pair.Second.Key)
                && pair.First.Label == pair.Second.Label.Trim()
                && pair.First.Choices == string.Join('\n', pair.Second.Choices ?? [])
            );

    private async Task<PlayQueueOverlayState?> ReadOverlayStateCoreAsync(
        int hostId,
        int queueId,
        int currentLimit,
        int nextLimit,
        CancellationToken cancellationToken
    )
    {
        if (!await OverlayFeaturesAreEnabledAsync(hostId, cancellationToken))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var queue = await LoadQueueAsync(db, hostId, queueId, cancellationToken);
        if (queue is null)
        {
            return null;
        }

        var current = queue
            .Entries.Where(value => value.Status == PlayQueueEntryStatus.Selected)
            .OrderBy(value => value.Id)
            .Take(currentLimit)
            .Select(entry => ToOverlayEntry(queue, entry))
            .ToArray();
        var waiting = OrderedWaiting(queue.Entries).ToArray();
        var next = waiting.Take(nextLimit).Select(entry => ToOverlayEntry(queue, entry)).ToArray();
        return new PlayQueueOverlayState(
            queue.Name,
            queue.ActivityName,
            queue.IsOpen,
            waiting.Length,
            current,
            next
        );
    }

    private static PlayQueueOverlayEntry ToOverlayEntry(PlayQueue queue, PlayQueueEntry entry) =>
        new(queue.ShowParticipantNames ? entry.DisplayName : null, PublicFields(queue, entry));

    private static IReadOnlyList<PlayQueueEntryFieldView> PublicFields(
        PlayQueue queue,
        PlayQueueEntry entry
    ) =>
        queue
            .Fields.OrderBy(field => field.Position)
            .Select(field => new PlayQueueEntryFieldView(
                field.Key,
                field.Label,
                entry.Values.FirstOrDefault(value => value.FieldId == field.Id)?.Value
                    ?? string.Empty
            ))
            .ToArray();

    private static IReadOnlyList<PlayQueueCommittedChange> CommittedChanges(
        BlokeBotDbContext db,
        int hostId
    )
    {
        var eventsByQueue = db
            .ChangeTracker.Entries<PlayQueueDomainEvent>()
            .Select(entry => entry.Entity)
            .Where(value => value.HostId == hostId)
            .GroupBy(value => value.QueueId)
            .ToDictionary(
                group => group.Key,
                group => TransitionFor(group.Select(value => value.Kind))
            );
        var queueIds = db
            .ChangeTracker.Entries<PlayQueue>()
            .Select(entry => entry.Entity)
            .Where(value => value.HostId == hostId && value.Id > 0)
            .Select(value => value.Id)
            .Concat(
                db.ChangeTracker.Entries<PlayQueueEntry>()
                    .Select(entry => entry.Entity)
                    .Where(value => value.HostId == hostId && value.QueueId > 0)
                    .Select(value => value.QueueId)
            )
            .Concat(eventsByQueue.Keys)
            .Distinct()
            .ToArray();
        return queueIds
            .Select(queueId => new PlayQueueCommittedChange(
                hostId,
                queueId,
                eventsByQueue.GetValueOrDefault(queueId)
            ))
            .ToArray();
    }

    private static PlayQueueOverlayTransition TransitionFor(IEnumerable<PlayQueueEventKind> events)
    {
        var kinds = events.ToArray();
        if (kinds.Contains(PlayQueueEventKind.PartySelected))
        {
            return PlayQueueOverlayTransition.PartyChanged;
        }
        if (kinds.Any(kind => kind is PlayQueueEventKind.Ready or PlayQueueEventKind.NoShow))
        {
            return PlayQueueOverlayTransition.ReadyOutcome;
        }
        return kinds.Contains(PlayQueueEventKind.ReadyCheckStarted)
            ? PlayQueueOverlayTransition.SelectedNext
            : PlayQueueOverlayTransition.None;
    }

    private async ValueTask NotifyOverlayChangesAsync(
        IReadOnlyList<PlayQueueCommittedChange> committedChanges,
        CancellationToken cancellationToken
    )
    {
        foreach (var change in committedChanges)
        {
            await _changes.NotifyAsync(change, cancellationToken);
        }
    }

    private static string PreferredRole(PlayQueueEntry entry) =>
        entry.Values.FirstOrDefault(value => value.Field?.Key == "preferred-role")?.Value
        ?? string.Empty;

    private static IReadOnlyList<string> Choices(string value) =>
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddEvent(
        BlokeBotDbContext db,
        PlayQueue queue,
        PlayQueueEntry? entry,
        PlayQueueEventKind kind,
        object payload,
        DateTime now
    )
    {
        var json = JsonSerializer.Serialize(payload);
        if (json.Length > _maximumEventPayloadLength)
        {
            throw new InvalidOperationException(
                "Queue lifecycle event payload exceeded its bound."
            );
        }

        db.PlayQueueEvents.Add(
            new PlayQueueDomainEvent
            {
                HostId = queue.HostId,
                QueueId = queue.Id,
                EntryId = entry?.Id,
                SchemaVersion = _eventSchemaVersion,
                Kind = kind,
                PublicPayload = json,
                OccurredAtUtc = now,
            }
        );
    }

    private SemaphoreSlim GateFor(int hostId, string slug)
    {
        _ = slug;
        return _mutationGates[(hostId & int.MaxValue) % _mutationGates.Length];
    }

    private static SemaphoreSlim[] CreateGates() =>
        Enumerable.Range(0, _gateCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static PlayQueueResult<T>.Succeeded Succeeded<T>(T value) => new(value);

    private static PlayQueueResult<T>.Rejected Rejected<T>(PlayQueueRejection reason) =>
        new(reason);

    private sealed record ConfigurationValues(
        IReadOnlyList<(PlayQueueField Field, string Value)> Values,
        PlayQueueRejection? Rejection
    );

    private sealed record CandidateSelection(
        IReadOnlyList<PlayQueueEntry> Members,
        PlayQueueRejection? Rejection
    );
}
