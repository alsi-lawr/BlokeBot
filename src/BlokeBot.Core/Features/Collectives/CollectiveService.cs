using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Core.Features.Collectives;

public sealed class CollectiveService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IRaidCollaborationProvider raidProvider,
    TimeProvider timeProvider
) : ICompetitionLifecycleObserver, IBountyChangeObserver, IRaidCollaborationDomainEventObserver
{
    private const int _auditLimit = 40;
    private const int _relayHistoryLimit = 20;
    private const int _maximumMembers = 50;
    private const long _maximumGoalValue = 1_000_000_000;

    public async Task<CollectiveDashboardOutcome> LoadAsync(
        CollectiveAuthority authority,
        CollectiveId? selectedCollectiveId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == authority.SelectedHostId, ct);
        if (host is null)
        {
            return new CollectiveDashboardOutcome.HostNotFound();
        }
        if (!AcceptsCurrentWork(host, null))
        {
            return new CollectiveDashboardOutcome.FeatureDisabled();
        }

        var collectives = await db
            .Collectives.AsNoTracking()
            .Where(value =>
                value.Memberships.Any(member =>
                    member.HostId == host.Id
                    && (
                        member.Status == CollectiveMembershipStatus.Active
                        || member.Status == CollectiveMembershipStatus.Pending
                    )
                )
            )
            .OrderBy(value => value.Name)
            .Select(value => new CollectiveSummary(
                new(value.PublicId),
                value.Name,
                value.Memberships.Count(member =>
                    member.Status == CollectiveMembershipStatus.Active
                ),
                value.Memberships.Count(member =>
                    member.Status == CollectiveMembershipStatus.Pending
                ),
                (value.TournamentReference == null ? 0 : 1)
                    + (value.RaidRelay == null ? 0 : 1)
                    + (value.Goal == null ? 0 : 1),
                value.UpdatedAtUtc
            ))
            .ToArrayAsync(ct);
        var selection =
            selectedCollectiveId is { } requested && collectives.Any(value => value.Id == requested)
                ? requested
                : collectives.FirstOrDefault()?.Id;
        var dashboard = selection is { } selected
            ? await LoadDashboardAsync(db, authority, selected, ct)
            : null;
        var memberHostIds = dashboard?.Members.Select(value => value.HostId).ToArray() ?? [];
        var knownHosts = await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Id != host.Id
                && !memberHostIds.Contains(value.Id)
                && (value.EnabledFeatures & HostFeatureFlags.Collectives)
                    == HostFeatureFlags.Collectives
            )
            .OrderBy(value => value.DisplayName)
            .Select(value => new CollectiveKnownHost(
                value.Id,
                value.Login,
                value.DisplayName == string.Empty ? value.Login : value.DisplayName
            ))
            .ToArrayAsync(ct);
        var ownedBounties = host.EnabledFeatures.Contains(
            HostFeatureFlags.Bounties | HostFeatureFlags.Points
        )
            ? await db
                .Bounties.AsNoTracking()
                .Where(value =>
                    value.HostId == host.Id && value.Visibility == BountyVisibility.Public
                )
                .OrderBy(value => value.Title)
                .Select(value => new CollectiveBountyChoice(value.PublicId, value.Title))
                .ToArrayAsync(ct)
            : [];
        return new CollectiveDashboardOutcome.Loaded(
            new(collectives, knownHosts, ownedBounties, selection, dashboard)
        );
    }

    public async Task<CollectivePublicProjection?> LoadPublicAsync(
        string hostLogin,
        CollectiveId collectiveId,
        CancellationToken ct
    )
    {
        var normalized = hostLogin.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var requestedHost = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.Login == normalized
                    && (value.EnabledFeatures & HostFeatureFlags.Collectives)
                        == HostFeatureFlags.Collectives,
                ct
            );
        if (requestedHost is null)
        {
            return null;
        }
        var membership = await db
            .CollectiveMemberships.AsNoTracking()
            .AnyAsync(
                value =>
                    value.Collective.PublicId == collectiveId.Value
                    && value.HostId == requestedHost.Id
                    && value.Status == CollectiveMembershipStatus.Active,
                ct
            );
        if (!membership)
        {
            return null;
        }

        var dashboard = await LoadDashboardAsync(
            db,
            new(requestedHost.Id, string.Empty, requestedHost.Login, false),
            collectiveId,
            ct
        );
        if (dashboard is null)
        {
            return null;
        }
        var enabledHosts = await db
            .Hosts.AsNoTracking()
            .Where(value =>
                (value.EnabledFeatures & HostFeatureFlags.Collectives)
                == HostFeatureFlags.Collectives
            )
            .Select(value => value.Id)
            .ToArrayAsync(ct);
        var enabled = enabledHosts.ToHashSet();
        var activeMembers = dashboard
            .Members.Where(value =>
                value.Status == CollectiveMembershipStatus.Active && enabled.Contains(value.HostId)
            )
            .ToArray();
        var activeLogins = activeMembers.Select(value => value.Login).ToHashSet();
        var tournament =
            dashboard.Tournament is { } tournamentReference
            && activeLogins.Contains(tournamentReference.OwnerLogin)
                ? tournamentReference
                : null;
        var relay =
            dashboard.RaidRelay is { } raidRelay
            && activeLogins.Contains(raidRelay.CurrentHostLogin)
                ? raidRelay with
                {
                    NextHostLogin =
                        raidRelay.NextHostLogin is { } next && activeLogins.Contains(next)
                            ? next
                            : null,
                    History = raidRelay
                        .History.Where(value =>
                            activeLogins.Contains(value.FromHostLogin)
                            && activeLogins.Contains(value.ToHostLogin)
                        )
                        .ToArray(),
                }
                : null;
        var goal = dashboard.Goal is { } sharedGoal
            ? sharedGoal with
            {
                HostTotals = sharedGoal
                    .HostTotals.Where(value => activeLogins.Contains(value.HostLogin))
                    .ToArray(),
                Current = sharedGoal
                    .HostTotals.Where(value => activeLogins.Contains(value.HostLogin))
                    .Sum(value => value.Total),
            }
            : null;
        return new(
            dashboard.Id,
            dashboard.Name,
            activeMembers.Select(value => value.DisplayName).ToArray(),
            tournament,
            relay,
            goal
        );
    }

    public async Task<CollectiveMutationOutcome> CreateAsync(
        CreateCollectiveCommand command,
        CancellationToken ct
    )
    {
        if (!ValidName(command.Name))
        {
            return new CollectiveMutationOutcome.Invalid(
                "Collective names must contain between 1 and 160 characters."
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var gate = await RequireAuthorityAsync(db, command.Authority, null, ct);
        if (gate is not null)
        {
            return gate;
        }
        var existing = await db
            .Collectives.AsNoTracking()
            .SingleOrDefaultAsync(value => value.CreationOperationId == command.OperationId, ct);
        if (existing is not null)
        {
            return new CollectiveMutationOutcome.Succeeded(new(existing.PublicId), true);
        }

        var now = UtcNow();
        var collective = new Collective
        {
            PublicId = Guid.NewGuid(),
            CreationOperationId = command.OperationId,
            Name = command.Name.Trim(),
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Memberships =
            [
                new()
                {
                    HostId = command.Authority.SelectedHostId,
                    Role = CollectiveMembershipRole.Coordinator,
                    Status = CollectiveMembershipStatus.Active,
                    AcceptWorkAfterUtc = now,
                    InvitedAtUtc = now,
                    RespondedAtUtc = now,
                    UpdatedAtUtc = now,
                },
            ],
        };
        AddAudit(
            collective,
            Operation(command.OperationId),
            CollectiveAuditAction.Created,
            command.Authority,
            command.Authority.SelectedHostId,
            now
        );
        _ = db.Collectives.Add(collective);
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new CollectiveMutationOutcome.Succeeded(new(collective.PublicId));
    }

    public Task<CollectiveMutationOutcome> InviteAsync(
        CollectiveMembershipCommand command,
        CancellationToken ct
    ) => ChangeMembershipAsync(command, MembershipChange.Invite, ct);

    public Task<CollectiveMutationOutcome> WithdrawInvitationAsync(
        CollectiveMembershipCommand command,
        CancellationToken ct
    ) => ChangeMembershipAsync(command, MembershipChange.Withdraw, ct);

    public Task<CollectiveMutationOutcome> RevokeAsync(
        CollectiveMembershipCommand command,
        CancellationToken ct
    ) => ChangeMembershipAsync(command, MembershipChange.Revoke, ct);

    public Task<CollectiveMutationOutcome> AcceptInvitationAsync(
        CollectiveSelfMembershipCommand command,
        CancellationToken ct
    ) => RespondToInvitationAsync(command, accept: true, ct);

    public Task<CollectiveMutationOutcome> DeclineInvitationAsync(
        CollectiveSelfMembershipCommand command,
        CancellationToken ct
    ) => RespondToInvitationAsync(command, accept: false, ct);

    public Task<CollectiveMutationOutcome> LeaveAsync(
        CollectiveSelfMembershipCommand command,
        CancellationToken ct
    ) => LeaveCoreAsync(command, ct);

    public async Task<CollectiveMutationOutcome> TransferCoordinationAsync(
        CollectiveMembershipCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var loaded = await LoadForCoordinatorMutationAsync(
            db,
            command.CollectiveId,
            command.Authority,
            command.OperationId,
            CollectiveAuditAction.CoordinationTransferred,
            ct
        );
        if (loaded.Outcome is not null)
        {
            return loaded.Outcome;
        }
        var collective = loaded.Collective!;
        var target = collective.Memberships.SingleOrDefault(value =>
            value.HostId == command.AffectedHostId
            && value.Status == CollectiveMembershipStatus.Active
        );
        if (target is null)
        {
            return new CollectiveMutationOutcome.Conflict(
                "Coordination can transfer only to an active member."
            );
        }
        if (await RequireEnabledHostAsync(db, target.HostId, null, ct) is { } disabled)
        {
            return disabled;
        }
        var actor = collective.Memberships.Single(value =>
            value.HostId == command.Authority.SelectedHostId
        );
        target.Role = CollectiveMembershipRole.Coordinator;
        actor.Role = CollectiveMembershipRole.Participant;
        var now = UtcNow();
        target.UpdatedAtUtc = now;
        actor.UpdatedAtUtc = now;
        Touch(collective, now);
        AddAudit(
            collective,
            Operation(command.OperationId),
            CollectiveAuditAction.CoordinationTransferred,
            command.Authority,
            target.HostId,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Success(command.CollectiveId);
    }

    public async Task<CollectiveMutationOutcome> SetTournamentReferenceAsync(
        SetTournamentReferenceCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var loaded = await LoadForCoordinatorMutationAsync(
            db,
            command.CollectiveId,
            command.Authority,
            command.OperationId,
            CollectiveAuditAction.TournamentReferenceChanged,
            ct
        );
        if (loaded.Outcome is not null)
        {
            return loaded.Outcome;
        }
        var collective = loaded.Collective!;
        if (!ActiveMember(collective, command.OwnerHostId))
        {
            return new CollectiveMutationOutcome.Invalid(
                "Tournament owners must be active collective members."
            );
        }
        if (
            await RequireEnabledHostAsync(
                db,
                command.OwnerHostId,
                HostFeatureFlags.Competitions,
                ct
            ) is
            { } disabled
        )
        {
            return disabled;
        }
        var competition = await db
            .Competitions.Include(value => value.Entrants)
            .Include(value => value.Matches)
            .SingleOrDefaultAsync(
                value =>
                    value.HostId == command.OwnerHostId
                    && value.PublicId == command.CompetitionPublicId,
                ct
            );
        if (competition is null)
        {
            return new CollectiveMutationOutcome.NotFound();
        }

        var now = UtcNow();
        var reference = collective.TournamentReference ?? new CollectiveTournamentReference();
        if (collective.TournamentReference is null)
        {
            collective.TournamentReference = reference;
        }
        ApplyTournament(reference, competition, now);
        Touch(collective, now);
        AddAudit(
            collective,
            Operation(command.OperationId),
            CollectiveAuditAction.TournamentReferenceChanged,
            command.Authority,
            command.OwnerHostId,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Success(command.CollectiveId);
    }

    public async Task<CollectiveMutationOutcome> ConfigureRaidRelayAsync(
        ConfigureRaidRelayCommand command,
        CancellationToken ct
    )
    {
        if (!ValidName(command.Name))
        {
            return new CollectiveMutationOutcome.Invalid(
                "Relay names must contain between 1 and 160 characters."
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var loaded = await LoadForCoordinatorMutationAsync(
            db,
            command.CollectiveId,
            command.Authority,
            command.OperationId,
            CollectiveAuditAction.RaidRelayChanged,
            ct
        );
        if (loaded.Outcome is not null)
        {
            return loaded.Outcome;
        }
        var collective = loaded.Collective!;
        if (
            !ActiveMember(collective, command.CurrentHostId)
            || (command.NextHostId is { } next && !ActiveMember(collective, next))
        )
        {
            return new CollectiveMutationOutcome.Invalid(
                "Relay hosts must be active collective members."
            );
        }
        foreach (
            var hostId in new int?[] { command.CurrentHostId, command.NextHostId }.OfType<int>()
        )
        {
            if (
                await RequireEnabledHostAsync(db, hostId, HostFeatureFlags.RaidCollaboration, ct) is
                { } disabled
            )
            {
                return disabled;
            }
        }

        var now = UtcNow();
        var relay = collective.RaidRelay ?? new CollectiveRaidRelay();
        if (collective.RaidRelay is null)
        {
            collective.RaidRelay = relay;
        }
        relay.Name = command.Name.Trim();
        relay.CurrentHostId = command.CurrentHostId;
        relay.NextHostId = command.NextHostId;
        relay.Status = command.NextHostId is null
            ? CollectiveWorkflowStatus.Completed
            : CollectiveWorkflowStatus.Pending;
        relay.Revision++;
        relay.LastSourceEventAtUtc = now;
        relay.UpdatedAtUtc = now;
        Touch(collective, now);
        AddAudit(
            collective,
            Operation(command.OperationId),
            CollectiveAuditAction.RaidRelayChanged,
            command.Authority,
            command.CurrentHostId,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Success(command.CollectiveId);
    }

    public async Task<CollectiveMutationOutcome> ConfirmRaidHandoffAsync(
        ConfirmRaidHandoffCommand command,
        CancellationToken ct
    )
    {
        var claim = await ClaimRaidHandoffAsync(command, ct);
        if (claim.Outcome is not null)
        {
            return claim.Outcome;
        }
        if (
            !await FeatureAcceptsCurrentWorkAsync(
                command.Authority.SelectedHostId,
                claim.OccurredAtUtc,
                ct
            )
        )
        {
            return new CollectiveMutationOutcome.FeatureDisabled(command.Authority.SelectedHostId);
        }
        if (
            claim.TargetTwitchUserId is not { } targetTwitchUserId
            || claim.TargetLogin is not { } targetLogin
        )
        {
            return new CollectiveMutationOutcome.Invalid(
                "The relay handoff target is unavailable."
            );
        }
        var provider = await raidProvider.StartConfirmedRaidAsync(
            command.Authority.SelectedHostId,
            targetTwitchUserId,
            targetLogin,
            ct
        );
        var providerAccepted = provider is ConfirmedRaidStartOutcome.Started;
        await CompleteRaidHandoffAsync(command, providerAccepted, ct);
        return providerAccepted
            ? Success(command.CollectiveId)
            : new CollectiveMutationOutcome.ProviderRejected();
    }

    public async Task<CollectiveMutationOutcome> ConfigureGoalAsync(
        ConfigureCollectiveGoalCommand command,
        CancellationToken ct
    )
    {
        if (
            !ValidName(command.Name)
            || string.IsNullOrWhiteSpace(command.UnitName)
            || command.UnitName.Trim().Length > 64
            || command.Target is <= 0 or > _maximumGoalValue
            || command.DeadlineUtc <= UtcNow()
            || command.Sources.Count > 1
            || command.Sources.Select(value => value.HostId).Distinct().Count()
                != command.Sources.Count
        )
        {
            return new CollectiveMutationOutcome.Invalid(
                "Shared goals require a name, unit, future deadline, bounded target, and at most the selected host's own source."
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var loaded = await LoadForCoordinatorMutationAsync(
            db,
            command.CollectiveId,
            command.Authority,
            command.OperationId,
            CollectiveAuditAction.GoalChanged,
            ct
        );
        if (loaded.Outcome is not null)
        {
            return loaded.Outcome;
        }
        var collective = loaded.Collective!;
        if (command.Sources.Any(value => value.HostId != command.Authority.SelectedHostId))
        {
            return new CollectiveMutationOutcome.AuthorityRequired();
        }
        var now = UtcNow();
        var goal = collective.Goal ?? new CollectiveGoal();
        if (collective.Goal is null)
        {
            collective.Goal = goal;
        }
        foreach (var source in command.Sources)
        {
            if (
                await RequireEnabledHostAsync(
                    db,
                    source.HostId,
                    HostFeatureFlags.Bounties | HostFeatureFlags.Points,
                    ct
                ) is
                { } disabled
            )
            {
                return disabled;
            }
            var bounty = await db
                .Bounties.AsNoTracking()
                .SingleOrDefaultAsync(
                    value =>
                        value.HostId == source.HostId
                        && value.PublicId == source.BountyPublicId
                        && value.Visibility == BountyVisibility.Public,
                    ct
                );
            if (bounty is null || !TryBoundedTotal(bounty.PledgedAmount, out var total))
            {
                return new CollectiveMutationOutcome.Invalid(
                    "The selected source must be a public bounded bounty owned by this host."
                );
            }
            var hostTotal = goal.HostTotals.SingleOrDefault(value => value.HostId == source.HostId);
            if (hostTotal is null)
            {
                hostTotal = new() { HostId = source.HostId };
                goal.HostTotals.Add(hostTotal);
            }
            hostTotal.SourceBountyPublicId = source.BountyPublicId;
            hostTotal.Total = total;
            hostTotal.LastSourceEventAtUtc = bounty.UpdatedAtUtc;
        }
        goal.Name = command.Name.Trim();
        goal.UnitName = command.UnitName.Trim();
        goal.Target = command.Target;
        goal.Current = goal.HostTotals.Sum(value => value.Total);
        goal.DeadlineUtc = DateTime.SpecifyKind(command.DeadlineUtc, DateTimeKind.Utc);
        goal.Status = GoalStatus(goal.Current, goal.Target, goal.DeadlineUtc, now);
        goal.Revision++;
        goal.UpdatedAtUtc = now;
        Touch(collective, now);
        AddAudit(
            collective,
            Operation(command.OperationId),
            CollectiveAuditAction.GoalChanged,
            command.Authority,
            null,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Success(command.CollectiveId);
    }

    public async Task<CollectiveMutationOutcome> SetGoalSourceAsync(
        SetCollectiveGoalSourceCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var gate = await RequireAuthorityAsync(
            db,
            command.Authority,
            HostFeatureFlags.Bounties | HostFeatureFlags.Points,
            ct
        );
        if (gate is not null)
        {
            return gate;
        }
        var collective = await LoadCollectiveForMutationAsync(db, command.CollectiveId, ct);
        if (collective?.Goal is not { } goal)
        {
            return collective is null
                ? new CollectiveMutationOutcome.NotFound()
                : new CollectiveMutationOutcome.Conflict("Configure the shared goal first.");
        }
        if (ExistingAudit(collective, command.OperationId) is { } existing)
        {
            return existing.Action == CollectiveAuditAction.GoalSourceChanged
                ? new CollectiveMutationOutcome.Succeeded(command.CollectiveId, true)
                : new CollectiveMutationOutcome.Conflict(
                    "That operation identity belongs to another collective change."
                );
        }
        if (!ActiveMember(collective, command.Authority.SelectedHostId))
        {
            return new CollectiveMutationOutcome.AuthorityRequired();
        }
        var bounty = await db
            .Bounties.AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.HostId == command.Authority.SelectedHostId
                    && value.PublicId == command.BountyPublicId
                    && value.Visibility == BountyVisibility.Public,
                ct
            );
        if (bounty is null || !TryBoundedTotal(bounty.PledgedAmount, out var total))
        {
            return new CollectiveMutationOutcome.Invalid(
                "Choose a public bounded bounty owned by the selected host."
            );
        }
        var hostTotal = goal.HostTotals.SingleOrDefault(value =>
            value.HostId == command.Authority.SelectedHostId
        );
        if (hostTotal is null)
        {
            hostTotal = new() { HostId = command.Authority.SelectedHostId };
            goal.HostTotals.Add(hostTotal);
        }
        var now = UtcNow();
        hostTotal.SourceBountyPublicId = command.BountyPublicId;
        hostTotal.Total = total;
        hostTotal.LastSourceEventAtUtc = bounty.UpdatedAtUtc;
        goal.Current = goal.HostTotals.Sum(value => value.Total);
        goal.Status = GoalStatus(goal.Current, goal.Target, goal.DeadlineUtc, now);
        goal.Revision++;
        goal.UpdatedAtUtc = now;
        Touch(collective, now);
        AddAudit(
            collective,
            Operation(command.OperationId),
            CollectiveAuditAction.GoalSourceChanged,
            command.Authority,
            command.Authority.SelectedHostId,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Success(command.CollectiveId);
    }

    public async Task<CollectiveMutationOutcome> SaveLocalSettingsAsync(
        SaveCollectiveLocalSettingsCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var gate = await RequireAuthorityAsync(db, command.Authority, null, ct);
        if (gate is not null)
        {
            return gate;
        }
        var collective = await db
            .Collectives.Include(value => value.Memberships)
            .Include(value => value.Audits)
            .SingleOrDefaultAsync(value => value.PublicId == command.CollectiveId.Value, ct);
        if (collective is null || !ActiveMember(collective, command.Authority.SelectedHostId))
        {
            return new CollectiveMutationOutcome.NotFound();
        }
        if (ExistingAudit(collective, command.OperationId) is { } existing)
        {
            return existing.Action == CollectiveAuditAction.LocalSettingsChanged
                ? new CollectiveMutationOutcome.Succeeded(command.CollectiveId, true)
                : new CollectiveMutationOutcome.Conflict(
                    "That operation identity belongs to another collective change."
                );
        }
        var settings = await db.CollectiveLocalSettings.SingleOrDefaultAsync(
            value =>
                value.CollectiveId == collective.Id
                && value.HostId == command.Authority.SelectedHostId,
            ct
        );
        var revision = settings?.Revision ?? 0;
        if (revision != command.ExpectedRevision)
        {
            return new CollectiveMutationOutcome.Conflict(
                $"Local settings changed at revision {revision}."
            );
        }
        settings ??= new CollectiveLocalSetting
        {
            CollectiveId = collective.Id,
            HostId = command.Authority.SelectedHostId,
        };
        if (settings.Id == 0)
        {
            _ = db.CollectiveLocalSettings.Add(settings);
        }
        var now = UtcNow();
        settings.Notification = command.Notification;
        settings.Revision++;
        settings.UpdatedAtUtc = now;
        AddAudit(
            collective,
            Operation(command.OperationId),
            CollectiveAuditAction.LocalSettingsChanged,
            command.Authority,
            command.Authority.SelectedHostId,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Success(command.CollectiveId);
    }

    public async ValueTask CompetitionChangedAsync(
        CompetitionLifecycleEvent competitionEvent,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == competitionEvent.HostId, cancellationToken);
        if (!AcceptsCurrentWork(host, competitionEvent.OccurredAtUtc.UtcDateTime))
        {
            return;
        }
        var references = await db
            .CollectiveTournamentReferences.Include(value => value.Collective)
                .ThenInclude(value => value.Memberships)
            .Include(value => value.Collective)
                .ThenInclude(value => value.Audits)
            .Where(value =>
                value.OwnerHostId == competitionEvent.HostId
                && value.CompetitionPublicId == competitionEvent.CompetitionId.Value
            )
            .ToArrayAsync(cancellationToken);
        if (references.Length == 0)
        {
            return;
        }
        var competition = await db
            .Competitions.Include(value => value.Entrants)
            .Include(value => value.Matches)
            .SingleAsync(
                value =>
                    value.HostId == competitionEvent.HostId
                    && value.PublicId == competitionEvent.CompetitionId.Value,
                cancellationToken
            );
        foreach (var reference in references)
        {
            if (
                !ActiveMemberAcceptsWork(
                    reference.Collective,
                    competitionEvent.HostId,
                    competitionEvent.OccurredAtUtc.UtcDateTime
                )
                || competitionEvent.OccurredAtUtc.UtcDateTime <= reference.LastSourceEventAtUtc
            )
            {
                continue;
            }
            ApplyTournament(reference, competition, competitionEvent.OccurredAtUtc.UtcDateTime);
            Touch(reference.Collective, competitionEvent.OccurredAtUtc.UtcDateTime);
            AddSystemAudit(
                reference.Collective,
                $"competition:{competitionEvent.OccurrenceId:N}",
                CollectiveAuditAction.TournamentReferenceChanged,
                competitionEvent.HostId,
                competitionEvent.OccurredAtUtc.UtcDateTime
            );
        }
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask BountyChangedAsync(int hostId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == hostId, cancellationToken);
        if (!AcceptsCurrentWork(host, null))
        {
            return;
        }
        var totals = await db
            .CollectiveGoalHostTotals.Include(value => value.Goal)
                .ThenInclude(value => value.Collective)
                    .ThenInclude(value => value.Memberships)
            .Include(value => value.Goal)
                .ThenInclude(value => value.Collective)
                    .ThenInclude(value => value.Audits)
            .Where(value => value.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        foreach (var total in totals)
        {
            var bounty = await db
                .Bounties.AsNoTracking()
                .SingleOrDefaultAsync(
                    value =>
                        value.HostId == hostId
                        && value.PublicId == total.SourceBountyPublicId
                        && value.Visibility == BountyVisibility.Public,
                    cancellationToken
                );
            if (
                bounty is null
                || bounty.UpdatedAtUtc <= total.LastSourceEventAtUtc
                || !TryBoundedTotal(bounty.PledgedAmount, out var current)
                || !ActiveMemberAcceptsWork(total.Goal.Collective, hostId, bounty.UpdatedAtUtc)
                || !AcceptsCurrentWork(host, bounty.UpdatedAtUtc)
            )
            {
                continue;
            }
            total.Total = current;
            total.LastSourceEventAtUtc = bounty.UpdatedAtUtc;
            total.Goal.Current = total.Goal.HostTotals.Sum(value =>
                value.Id == total.Id ? current : value.Total
            );
            total.Goal.Status = GoalStatus(
                total.Goal.Current,
                total.Goal.Target,
                total.Goal.DeadlineUtc,
                bounty.UpdatedAtUtc
            );
            total.Goal.Revision++;
            total.Goal.UpdatedAtUtc = bounty.UpdatedAtUtc;
            Touch(total.Goal.Collective, bounty.UpdatedAtUtc);
            AddSystemAudit(
                total.Goal.Collective,
                $"bounty:{hostId}:{bounty.PublicId:N}:{bounty.Revision}",
                CollectiveAuditAction.GoalProgressChanged,
                hostId,
                bounty.UpdatedAtUtc
            );
        }
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask CollaborationEventAsync(
        RaidCollaborationDomainEvent domainEvent,
        CancellationToken cancellationToken
    )
    {
        if (domainEvent.Kind != RaidCollaborationDomainEventKind.OutgoingRaidRecorded)
        {
            return;
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == domainEvent.HostId, cancellationToken);
        if (source is null || !AcceptsCurrentWork(source, domainEvent.OccurredAt.UtcDateTime))
        {
            return;
        }
        var target = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.TwitchUserId == domainEvent.ChannelTwitchUserId,
                cancellationToken
            );
        if (target is null || !AcceptsCurrentWork(target, domainEvent.OccurredAt.UtcDateTime))
        {
            return;
        }
        var relays = await db
            .CollectiveRaidRelays.Include(value => value.Collective)
                .ThenInclude(value => value.Memberships)
            .Include(value => value.Collective)
                .ThenInclude(value => value.Audits)
            .Include(value => value.Handoffs)
            .Where(value =>
                value.CurrentHostId == domainEvent.HostId && value.NextHostId == target.Id
            )
            .ToArrayAsync(cancellationToken);
        foreach (var relay in relays)
        {
            var operation = $"raid:{domainEvent.ProviderMessageId}";
            if (
                relay.Handoffs.Any(value => value.OperationId == operation)
                || domainEvent.OccurredAt.UtcDateTime < relay.LastSourceEventAtUtc
                || !ActiveMemberAcceptsWork(
                    relay.Collective,
                    source.Id,
                    domainEvent.OccurredAt.UtcDateTime
                )
                || !ActiveMemberAcceptsWork(
                    relay.Collective,
                    target.Id,
                    domainEvent.OccurredAt.UtcDateTime
                )
            )
            {
                continue;
            }
            relay.Handoffs.Add(
                new()
                {
                    OperationId = operation,
                    FromHostId = source.Id,
                    ToHostId = target.Id,
                    AggregateViewerCount = domainEvent.ViewerCount,
                    Status = CollectiveRaidHandoffStatus.Confirmed,
                    OccurredAtUtc = domainEvent.OccurredAt.UtcDateTime,
                    UpdatedAtUtc = UtcNow(),
                }
            );
            relay.AggregateViewerCount = domainEvent.ViewerCount;
            relay.CurrentHostId = target.Id;
            relay.NextHostId = null;
            relay.Status = CollectiveWorkflowStatus.Completed;
            relay.Revision++;
            relay.LastSourceEventAtUtc = domainEvent.OccurredAt.UtcDateTime;
            relay.UpdatedAtUtc = UtcNow();
            Touch(relay.Collective, UtcNow());
            AddSystemAudit(
                relay.Collective,
                operation,
                CollectiveAuditAction.RaidHandoffConfirmed,
                source.Id,
                domainEvent.OccurredAt.UtcDateTime,
                target.Id
            );
        }
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<CollectiveMutationOutcome> ChangeMembershipAsync(
        CollectiveMembershipCommand command,
        MembershipChange change,
        CancellationToken ct
    )
    {
        var action = change switch
        {
            MembershipChange.Invite => CollectiveAuditAction.HostInvited,
            MembershipChange.Withdraw => CollectiveAuditAction.InvitationWithdrawn,
            MembershipChange.Revoke => CollectiveAuditAction.MemberRevoked,
            _ => throw new UnreachableException(),
        };
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var loaded = await LoadForCoordinatorMutationAsync(
            db,
            command.CollectiveId,
            command.Authority,
            command.OperationId,
            action,
            ct
        );
        if (loaded.Outcome is not null)
        {
            return loaded.Outcome;
        }
        var collective = loaded.Collective!;
        if (command.AffectedHostId == command.Authority.SelectedHostId)
        {
            return new CollectiveMutationOutcome.Invalid(
                "Use the current host leave action for its own membership."
            );
        }
        if (await RequireEnabledHostAsync(db, command.AffectedHostId, null, ct) is { } disabled)
        {
            return disabled;
        }
        var targetHostExists = await db
            .Hosts.AsNoTracking()
            .AnyAsync(value => value.Id == command.AffectedHostId, ct);
        if (!targetHostExists)
        {
            return new CollectiveMutationOutcome.NotFound();
        }
        var membership = collective.Memberships.SingleOrDefault(value =>
            value.HostId == command.AffectedHostId
        );
        var now = UtcNow();
        if (change == MembershipChange.Invite)
        {
            if (
                collective.Memberships.Count(value =>
                    value.Status
                        is CollectiveMembershipStatus.Active
                            or CollectiveMembershipStatus.Pending
                ) >= _maximumMembers
            )
            {
                return new CollectiveMutationOutcome.Conflict(
                    $"Collectives support at most {_maximumMembers} active or invited hosts."
                );
            }
            if (
                membership?.Status
                is CollectiveMembershipStatus.Active
                    or CollectiveMembershipStatus.Pending
            )
            {
                return new CollectiveMutationOutcome.Conflict(
                    "That host already participates or has a pending invitation."
                );
            }
            membership ??= new CollectiveMembership
            {
                HostId = command.AffectedHostId,
                Role = CollectiveMembershipRole.Participant,
            };
            if (membership.Id == 0)
            {
                collective.Memberships.Add(membership);
            }
            membership.Status = CollectiveMembershipStatus.Pending;
            membership.AcceptWorkAfterUtc = now;
            membership.InvitedAtUtc = now;
            membership.RespondedAtUtc = null;
            membership.UpdatedAtUtc = now;
        }
        else if (
            change == MembershipChange.Withdraw
            && membership?.Status == CollectiveMembershipStatus.Pending
        )
        {
            membership.Status = CollectiveMembershipStatus.Revoked;
            membership.RespondedAtUtc = now;
            membership.UpdatedAtUtc = now;
        }
        else if (
            change == MembershipChange.Revoke
            && membership?.Status == CollectiveMembershipStatus.Active
        )
        {
            if (LastCoordinator(collective, membership))
            {
                return new CollectiveMutationOutcome.LastCoordinatorRequired();
            }
            membership.Status = CollectiveMembershipStatus.Revoked;
            membership.RespondedAtUtc = now;
            membership.UpdatedAtUtc = now;
        }
        else
        {
            return new CollectiveMutationOutcome.Conflict(
                "The membership is not in a state that supports this change."
            );
        }
        Touch(collective, now);
        AddAudit(
            collective,
            Operation(command.OperationId),
            action,
            command.Authority,
            command.AffectedHostId,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Success(command.CollectiveId);
    }

    private async Task<CollectiveMutationOutcome> RespondToInvitationAsync(
        CollectiveSelfMembershipCommand command,
        bool accept,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var gate = await RequireAuthorityAsync(db, command.Authority, null, ct);
        if (gate is not null)
        {
            return gate;
        }
        var collective = await LoadCollectiveForMutationAsync(db, command.CollectiveId, ct);
        if (collective is null)
        {
            return new CollectiveMutationOutcome.NotFound();
        }
        if (ExistingAudit(collective, command.OperationId) is { } existing)
        {
            var expected = accept
                ? CollectiveAuditAction.InvitationAccepted
                : CollectiveAuditAction.InvitationDeclined;
            return existing.Action == expected
                ? new CollectiveMutationOutcome.Succeeded(command.CollectiveId, true)
                : new CollectiveMutationOutcome.Conflict(
                    "That operation identity belongs to another collective change."
                );
        }
        var membership = collective.Memberships.SingleOrDefault(value =>
            value.HostId == command.Authority.SelectedHostId
        );
        if (membership?.Status != CollectiveMembershipStatus.Pending)
        {
            return new CollectiveMutationOutcome.Conflict(
                "Only the invited selected host can answer a pending invitation."
            );
        }
        var now = UtcNow();
        membership.Status = accept
            ? CollectiveMembershipStatus.Active
            : CollectiveMembershipStatus.Declined;
        membership.AcceptWorkAfterUtc = now;
        membership.RespondedAtUtc = now;
        membership.UpdatedAtUtc = now;
        Touch(collective, now);
        AddAudit(
            collective,
            Operation(command.OperationId),
            accept
                ? CollectiveAuditAction.InvitationAccepted
                : CollectiveAuditAction.InvitationDeclined,
            command.Authority,
            membership.HostId,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Success(command.CollectiveId);
    }

    private async Task<CollectiveMutationOutcome> LeaveCoreAsync(
        CollectiveSelfMembershipCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var gate = await RequireAuthorityAsync(db, command.Authority, null, ct);
        if (gate is not null)
        {
            return gate;
        }
        var collective = await LoadCollectiveForMutationAsync(db, command.CollectiveId, ct);
        if (collective is null)
        {
            return new CollectiveMutationOutcome.NotFound();
        }
        if (ExistingAudit(collective, command.OperationId) is { } existing)
        {
            return existing.Action == CollectiveAuditAction.MemberLeft
                ? new CollectiveMutationOutcome.Succeeded(command.CollectiveId, true)
                : new CollectiveMutationOutcome.Conflict(
                    "That operation identity belongs to another collective change."
                );
        }
        var membership = collective.Memberships.SingleOrDefault(value =>
            value.HostId == command.Authority.SelectedHostId
            && value.Status == CollectiveMembershipStatus.Active
        );
        if (membership is null)
        {
            return new CollectiveMutationOutcome.Conflict(
                "Only an active selected host can leave for itself."
            );
        }
        if (LastCoordinator(collective, membership))
        {
            return new CollectiveMutationOutcome.LastCoordinatorRequired();
        }
        var now = UtcNow();
        membership.Status = CollectiveMembershipStatus.Left;
        membership.RespondedAtUtc = now;
        membership.UpdatedAtUtc = now;
        Touch(collective, now);
        AddAudit(
            collective,
            Operation(command.OperationId),
            CollectiveAuditAction.MemberLeft,
            command.Authority,
            membership.HostId,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Success(command.CollectiveId);
    }

    private async Task<MutationLoad> LoadForCoordinatorMutationAsync(
        BlokeBotDbContext db,
        CollectiveId collectiveId,
        CollectiveAuthority authority,
        Guid operationId,
        CollectiveAuditAction expectedAction,
        CancellationToken ct
    )
    {
        var gate = await RequireAuthorityAsync(db, authority, null, ct);
        if (gate is not null)
        {
            return new(null, gate);
        }
        var collective = await LoadCollectiveForMutationAsync(db, collectiveId, ct);
        if (collective is null)
        {
            return new(null, new CollectiveMutationOutcome.NotFound());
        }
        if (ExistingAudit(collective, operationId) is { } existing)
        {
            return new(
                collective,
                existing.Action == expectedAction
                    ? new CollectiveMutationOutcome.Succeeded(collectiveId, true)
                    : new CollectiveMutationOutcome.Conflict(
                        "That operation identity belongs to another collective change."
                    )
            );
        }
        var membership = collective.Memberships.SingleOrDefault(value =>
            value.HostId == authority.SelectedHostId
            && value.Status == CollectiveMembershipStatus.Active
            && value.Role == CollectiveMembershipRole.Coordinator
        );
        return membership is null
            ? new(null, new CollectiveMutationOutcome.AuthorityRequired())
            : new(collective, null);
    }

    private static Task<Collective?> LoadCollectiveForMutationAsync(
        BlokeBotDbContext db,
        CollectiveId collectiveId,
        CancellationToken ct
    ) =>
        db
            .Collectives.Include(value => value.Memberships)
            .Include(value => value.Audits)
            .Include(value => value.TournamentReference)
            .Include(value => value.RaidRelay)
                .ThenInclude(value => value!.Handoffs)
            .Include(value => value.Goal)
                .ThenInclude(value => value!.HostTotals)
            .SingleOrDefaultAsync(value => value.PublicId == collectiveId.Value, ct);

    private async Task<CollectiveDashboard?> LoadDashboardAsync(
        BlokeBotDbContext db,
        CollectiveAuthority authority,
        CollectiveId collectiveId,
        CancellationToken ct
    )
    {
        var collective = await db
            .Collectives.AsNoTracking()
            .AsSplitQuery()
            .Include(value => value.Memberships)
            .Include(value => value.TournamentReference)
            .Include(value => value.RaidRelay)
                .ThenInclude(value => value!.Handoffs)
            .Include(value => value.Goal)
                .ThenInclude(value => value!.HostTotals)
            .Include(value =>
                value.Audits.OrderByDescending(audit => audit.OccurredAtUtc).Take(_auditLimit)
            )
            .SingleOrDefaultAsync(value => value.PublicId == collectiveId.Value, ct);
        if (
            collective is null
            || !collective.Memberships.Any(value =>
                value.HostId == authority.SelectedHostId
                && value.Status
                    is CollectiveMembershipStatus.Active
                        or CollectiveMembershipStatus.Pending
            )
        )
        {
            return null;
        }
        var hostIds = collective
            .Memberships.Select(value => value.HostId)
            .Concat(collective.Audits.Select(value => value.ActingHostId))
            .Concat(collective.Audits.Select(value => value.AffectedHostId ?? 0))
            .Where(value => value != 0)
            .Distinct()
            .ToArray();
        var hosts = await db
            .Hosts.AsNoTracking()
            .Where(value => hostIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, ct);
        var settings = await db
            .CollectiveLocalSettings.AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.CollectiveId == collective.Id && value.HostId == authority.SelectedHostId,
                ct
            );
        var members = collective
            .Memberships.OrderBy(value => value.HostId == authority.SelectedHostId ? 0 : 1)
            .ThenBy(value => value.Status == CollectiveMembershipStatus.Active ? 0 : 1)
            .ThenBy(value => hosts[value.HostId].DisplayName)
            .Select(value => new CollectiveMemberProjection(
                value.HostId,
                hosts[value.HostId].Login,
                DisplayName(hosts[value.HostId]),
                value.Role,
                value.Status,
                value.HostId == authority.SelectedHostId && authority.CanManageSelectedHost
            ))
            .ToArray();
        var tournament = collective.TournamentReference is { } reference
            ? new CollectiveTournamentProjection(
                hosts[reference.OwnerHostId].Login,
                reference.Name,
                reference.CompetitionPublicId,
                reference.Format,
                reference.Status,
                reference.Round,
                reference.EntrantCount,
                reference.ConfirmedResultCount,
                reference.Revision,
                reference.UpdatedAtUtc
            )
            : null;
        var relay = collective.RaidRelay is { } relayState
            ? new CollectiveRaidRelayProjection(
                relayState.Name,
                hosts[relayState.CurrentHostId].Login,
                relayState.NextHostId is { } next ? hosts[next].Login : null,
                relayState.AggregateViewerCount,
                relayState.Status,
                relayState.Revision,
                relayState
                    .Handoffs.OrderByDescending(value => value.OccurredAtUtc)
                    .Take(_relayHistoryLimit)
                    .Select(value => new CollectiveRaidHandoffProjection(
                        ShortOperation(value.OperationId),
                        hosts[value.FromHostId].Login,
                        hosts[value.ToHostId].Login,
                        value.AggregateViewerCount,
                        value.Status,
                        value.OccurredAtUtc
                    ))
                    .ToArray()
            )
            : null;
        var goal = collective.Goal is { } goalState
            ? new CollectiveGoalProjection(
                goalState.Name,
                goalState.UnitName,
                goalState.Target,
                goalState.Current,
                goalState.DeadlineUtc,
                goalState.Status,
                goalState.Revision,
                goalState
                    .HostTotals.OrderBy(value => hosts[value.HostId].DisplayName)
                    .Select(value => new CollectiveGoalHostProjection(
                        hosts[value.HostId].Login,
                        DisplayName(hosts[value.HostId]),
                        value.Total
                    ))
                    .ToArray()
            )
            : null;
        var selectedMembership = collective.Memberships.Single(value =>
            value.HostId == authority.SelectedHostId
        );
        var localGoalSourcePublicId = collective
            .Goal?.HostTotals.SingleOrDefault(value => value.HostId == authority.SelectedHostId)
            ?.SourceBountyPublicId;
        return new(
            new(collective.PublicId),
            collective.Name,
            collective.Revision,
            authority.CanManageSelectedHost
                && selectedMembership.Status == CollectiveMembershipStatus.Active
                && selectedMembership.Role == CollectiveMembershipRole.Coordinator,
            members,
            tournament,
            relay,
            goal,
            localGoalSourcePublicId,
            new(
                settings?.Notification ?? CollectiveLocalNotification.Moderators,
                settings?.Revision ?? 0
            ),
            collective
                .Audits.OrderByDescending(value => value.OccurredAtUtc)
                .Select(value => new CollectiveAuditProjection(
                    value.Action,
                    hosts[value.ActingHostId].Login,
                    value.AffectedHostId is { } affected ? hosts[affected].Login : null,
                    value.ActorLogin,
                    ShortOperation(value.OperationId),
                    value.OccurredAtUtc
                ))
                .ToArray()
        );
    }

    private async Task<RaidHandoffClaim> ClaimRaidHandoffAsync(
        ConfirmRaidHandoffCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var gate = await RequireAuthorityAsync(
            db,
            command.Authority,
            HostFeatureFlags.RaidCollaboration,
            ct
        );
        if (gate is not null)
        {
            return new(gate, null, null, default);
        }
        var collective = await LoadCollectiveForMutationAsync(db, command.CollectiveId, ct);
        if (collective?.RaidRelay is not { NextHostId: { } targetHostId } relay)
        {
            return new(new CollectiveMutationOutcome.NotFound(), null, null, default);
        }
        var operation = Operation(command.OperationId);
        var existing = relay.Handoffs.SingleOrDefault(value => value.OperationId == operation);
        if (existing is not null)
        {
            return new(
                existing.Status == CollectiveRaidHandoffStatus.Confirmed
                    ? new CollectiveMutationOutcome.Succeeded(command.CollectiveId, true)
                    : new CollectiveMutationOutcome.Conflict(
                        "That handoff operation has already been prepared."
                    ),
                null,
                null,
                existing.OccurredAtUtc
            );
        }
        if (
            relay.CurrentHostId != command.Authority.SelectedHostId
            || relay.Revision != command.ExpectedRevision
            || !ActiveMember(collective, targetHostId)
        )
        {
            return new(
                new CollectiveMutationOutcome.Conflict(
                    "The relay changed or the selected host does not own the current handoff."
                ),
                null,
                null,
                default
            );
        }
        if (
            await RequireEnabledHostAsync(
                db,
                targetHostId,
                HostFeatureFlags.RaidCollaboration,
                ct
            ) is
            { } disabled
        )
        {
            return new(disabled, null, null, default);
        }
        var target = await db
            .Hosts.AsNoTracking()
            .SingleAsync(value => value.Id == targetHostId, ct);
        if (string.IsNullOrWhiteSpace(target.TwitchUserId))
        {
            return new(
                new CollectiveMutationOutcome.Invalid(
                    "The next host does not have a provider identity."
                ),
                null,
                null,
                default
            );
        }
        var now = UtcNow();
        relay.Handoffs.Add(
            new()
            {
                OperationId = operation,
                FromHostId = command.Authority.SelectedHostId,
                ToHostId = targetHostId,
                AggregateViewerCount = relay.AggregateViewerCount,
                Status = CollectiveRaidHandoffStatus.Prepared,
                OccurredAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        AddAudit(
            collective,
            operation,
            CollectiveAuditAction.RaidHandoffPrepared,
            command.Authority,
            targetHostId,
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(null, target.TwitchUserId, target.Login, now);
    }

    private async Task CompleteRaidHandoffAsync(
        ConfirmRaidHandoffCommand command,
        bool providerAccepted,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await ImmediateTransaction.StartAsync(db, ct);
        var relay = await db
            .CollectiveRaidRelays.Include(value => value.Collective)
                .ThenInclude(value => value.Audits)
            .Include(value => value.Handoffs)
            .SingleAsync(value => value.Collective.PublicId == command.CollectiveId.Value, ct);
        var handoff = relay.Handoffs.Single(value =>
            value.OperationId == Operation(command.OperationId)
        );
        if (handoff.Status != CollectiveRaidHandoffStatus.Prepared)
        {
            return;
        }
        var now = UtcNow();
        handoff.Status = providerAccepted
            ? CollectiveRaidHandoffStatus.Confirmed
            : CollectiveRaidHandoffStatus.ProviderRejected;
        handoff.UpdatedAtUtc = now;
        if (providerAccepted)
        {
            relay.CurrentHostId = handoff.ToHostId;
            relay.NextHostId = null;
            relay.Status = CollectiveWorkflowStatus.Completed;
            relay.Revision++;
            relay.LastSourceEventAtUtc = now;
            relay.UpdatedAtUtc = now;
            Touch(relay.Collective, now);
            AddAudit(
                relay.Collective,
                $"{handoff.OperationId}:confirmed",
                CollectiveAuditAction.RaidHandoffConfirmed,
                command.Authority,
                handoff.ToHostId,
                now
            );
        }
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<CollectiveMutationOutcome?> RequireAuthorityAsync(
        BlokeBotDbContext db,
        CollectiveAuthority authority,
        HostFeatureFlags? additionalFeature,
        CancellationToken ct
    ) =>
        authority.SelectedHostId <= 0
        || !authority.CanManageSelectedHost
        || string.IsNullOrWhiteSpace(authority.Login)
            ? new CollectiveMutationOutcome.AuthorityRequired()
            : await RequireEnabledHostAsync(db, authority.SelectedHostId, additionalFeature, ct);

    private static async Task<CollectiveMutationOutcome?> RequireEnabledHostAsync(
        BlokeBotDbContext db,
        int hostId,
        HostFeatureFlags? additionalFeature,
        CancellationToken ct
    )
    {
        var required = HostFeatureFlags.Collectives | additionalFeature.GetValueOrDefault();
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == hostId, ct);
        return host is null ? new CollectiveMutationOutcome.NotFound()
            : (host.EnabledFeatures & required) != required
                ? new CollectiveMutationOutcome.FeatureDisabled(hostId)
            : null;
    }

    private async Task<bool> FeatureAcceptsCurrentWorkAsync(
        int hostId,
        DateTime occurredAtUtc,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == hostId, ct);
        return AcceptsCurrentWork(host, occurredAtUtc);
    }

    private static bool AcceptsCurrentWork(BotHost? host, DateTime? occurredAtUtc) =>
        host is not null
        && host.EnabledFeatures.Contains(HostFeatureFlags.Collectives)
        && (
            occurredAtUtc is null
            || host.CollectivesAcceptWorkAfterUtc is null
            || occurredAtUtc >= host.CollectivesAcceptWorkAfterUtc
        );

    private static void ApplyTournament(
        CollectiveTournamentReference reference,
        Competition competition,
        DateTime occurredAtUtc
    )
    {
        reference.OwnerHostId = competition.HostId;
        reference.CompetitionPublicId = competition.PublicId;
        reference.Name = competition.Name;
        reference.Format = competition.Format;
        reference.Status = competition.Status;
        reference.Round =
            competition.Matches.Count == 0 ? 0 : competition.Matches.Max(value => value.Round);
        reference.EntrantCount = competition.Entrants.Count;
        reference.ConfirmedResultCount = competition.Matches.Count(value =>
            value.Status == CompetitionMatchStatus.Confirmed
        );
        reference.Revision++;
        reference.LastSourceEventAtUtc = occurredAtUtc;
        reference.UpdatedAtUtc = occurredAtUtc;
    }

    private static CollectiveWorkflowStatus GoalStatus(
        long current,
        long target,
        DateTime deadlineUtc,
        DateTime nowUtc
    ) =>
        current >= target || nowUtc >= deadlineUtc
            ? CollectiveWorkflowStatus.Completed
            : CollectiveWorkflowStatus.Active;

    private static bool TryBoundedTotal(string value, out long total) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out total)
        && total is >= 0 and <= _maximumGoalValue;

    private static bool ActiveMember(Collective collective, int hostId) =>
        collective.Memberships.Any(value =>
            value.HostId == hostId && value.Status == CollectiveMembershipStatus.Active
        );

    private static bool ActiveMemberAcceptsWork(
        Collective collective,
        int hostId,
        DateTime occurredAtUtc
    ) =>
        collective.Memberships.Any(value =>
            value.HostId == hostId
            && value.Status == CollectiveMembershipStatus.Active
            && occurredAtUtc >= value.AcceptWorkAfterUtc
        );

    private static bool LastCoordinator(Collective collective, CollectiveMembership membership) =>
        membership.Role == CollectiveMembershipRole.Coordinator
        && collective.Memberships.Count(value =>
            value.Status == CollectiveMembershipStatus.Active
            && value.Role == CollectiveMembershipRole.Coordinator
        ) == 1;

    private static CollectiveAudit? ExistingAudit(Collective collective, Guid operationId) =>
        collective.Audits.SingleOrDefault(value => value.OperationId == Operation(operationId));

    private static void AddAudit(
        Collective collective,
        string operationId,
        CollectiveAuditAction action,
        CollectiveAuthority authority,
        int? affectedHostId,
        DateTime occurredAtUtc
    ) =>
        collective.Audits.Add(
            new()
            {
                OperationId = operationId,
                Action = action,
                ActingHostId = authority.SelectedHostId,
                AffectedHostId = affectedHostId,
                ActorTwitchUserId = authority.TwitchUserId.Trim(),
                ActorLogin = authority.Login.Trim().ToLowerInvariant(),
                OccurredAtUtc = occurredAtUtc,
            }
        );

    private static void AddSystemAudit(
        Collective collective,
        string operationId,
        CollectiveAuditAction action,
        int actingHostId,
        DateTime occurredAtUtc,
        int? affectedHostId = null
    ) =>
        collective.Audits.Add(
            new()
            {
                OperationId = operationId,
                Action = action,
                ActingHostId = actingHostId,
                AffectedHostId = affectedHostId,
                ActorLogin = "system",
                OccurredAtUtc = occurredAtUtc,
            }
        );

    private static void Touch(Collective collective, DateTime now)
    {
        collective.Revision++;
        collective.UpdatedAtUtc = now;
    }

    private static string DisplayName(BotHost host) =>
        string.IsNullOrWhiteSpace(host.DisplayName) ? host.Login : host.DisplayName;

    private static string Operation(Guid operationId) => operationId.ToString("N");

    private static string ShortOperation(string operationId) =>
        operationId.Length <= 8
            ? operationId.ToUpperInvariant()
            : operationId[^8..].ToUpperInvariant();

    private static bool ValidName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 160;

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static CollectiveMutationOutcome Success(CollectiveId collectiveId) =>
        new CollectiveMutationOutcome.Succeeded(collectiveId);

    private enum MembershipChange
    {
        Invite,
        Withdraw,
        Revoke,
    }

    private sealed record MutationLoad(Collective? Collective, CollectiveMutationOutcome? Outcome);

    private sealed record RaidHandoffClaim(
        CollectiveMutationOutcome? Outcome,
        string? TargetTwitchUserId,
        string? TargetLogin,
        DateTime OccurredAtUtc
    );

    private sealed class ImmediateTransaction(
        SqliteTransaction providerTransaction,
        IDbContextTransaction contextTransaction
    ) : IAsyncDisposable
    {
        public static async Task<ImmediateTransaction> StartAsync(
            BlokeBotDbContext db,
            CancellationToken ct
        )
        {
            await db.Database.OpenConnectionAsync(ct);
            var connection =
                db.Database.GetDbConnection() as SqliteConnection
                ?? throw new InvalidOperationException("Collective persistence requires SQLite.");
            var providerTransaction = connection.BeginTransaction(deferred: false);
            try
            {
                var contextTransaction =
                    await db.Database.UseTransactionAsync(providerTransaction, ct)
                    ?? throw new InvalidOperationException(
                        "The immediate SQLite transaction could not be attached."
                    );
                return new(providerTransaction, contextTransaction);
            }
            catch
            {
                await providerTransaction.DisposeAsync();
                throw;
            }
        }

        public Task CommitAsync(CancellationToken ct) => contextTransaction.CommitAsync(ct);

        public async ValueTask DisposeAsync()
        {
            await contextTransaction.DisposeAsync();
            await providerTransaction.DisposeAsync();
        }
    }
}
