using BlokeBot.Core.Features.Collectives;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CollectiveServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task MembershipAuthority_IdempotencyAuditAndLastCoordinator_AreBoundedPerHost()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var provider = new RecordingRaidProvider();
        var service = new CollectiveService(database, provider, new ManualTimeProvider(_now));
        var createOperation = Guid.NewGuid();
        var created = (
            await service.CreateAsync(
                new(createOperation, "Cosy Circuit", Authority(alpha, "alpha")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        var inviteOperation = Guid.NewGuid();

        _ = (
            await service.InviteAsync(
                new(inviteOperation, created.CollectiveId, beta, Authority(alpha, "alpha")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        (
            await service.InviteAsync(
                new(inviteOperation, created.CollectiveId, beta, Authority(alpha, "alpha")),
                default
            )
        )
            .ShouldBeOfType<CollectiveMutationOutcome.Succeeded>()
            .WasIdempotent.ShouldBeTrue();
        _ = (
            await service.AcceptInvitationAsync(
                new(Guid.NewGuid(), created.CollectiveId, Authority(alpha, "alpha")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Conflict>();
        _ = (
            await service.AcceptInvitationAsync(
                new(Guid.NewGuid(), created.CollectiveId, Authority(beta, "beta")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        _ = (
            await service.InviteAsync(
                new(Guid.NewGuid(), created.CollectiveId, alpha, Authority(beta, "beta")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.AuthorityRequired>();
        _ = (
            await service.LeaveAsync(
                new(Guid.NewGuid(), created.CollectiveId, Authority(alpha, "alpha")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.LastCoordinatorRequired>();
        _ = (
            await service.TransferCoordinationAsync(
                new(Guid.NewGuid(), created.CollectiveId, beta, Authority(alpha, "alpha")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        _ = (
            await service.LeaveAsync(
                new(Guid.NewGuid(), created.CollectiveId, Authority(alpha, "alpha")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();

        await using var verify = await database.CreateDbContextAsync();
        var collective = await verify
            .Collectives.Include(value => value.Memberships)
            .Include(value => value.Audits)
            .SingleAsync();
        collective
            .Memberships.Single(value => value.HostId == alpha)
            .Status.ShouldBe(CollectiveMembershipStatus.Left);
        collective
            .Memberships.Single(value => value.HostId == beta)
            .Role.ShouldBe(CollectiveMembershipRole.Coordinator);
        collective
            .Audits.Count(value => value.OperationId == inviteOperation.ToString("N"))
            .ShouldBe(1);
        collective.Audits.ShouldContain(value =>
            value.Action == CollectiveAuditAction.CoordinationTransferred
        );
    }

    [Test]
    public async Task FeatureDisable_RetainsStateBlocksMutationsAndDoesNotReplayGoalUpdates()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_now);
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var source = Guid.NewGuid();
        await SeedBountyAsync(database, alpha, source, 3, clock.GetUtcNow().UtcDateTime);
        var service = new CollectiveService(database, new RecordingRaidProvider(), clock);
        var collectiveId = await CreateWithMemberAsync(service, alpha, beta);
        _ = (
            await service.ConfigureGoalAsync(
                new(
                    Guid.NewGuid(),
                    collectiveId,
                    "Build comfort kits",
                    "kit",
                    12,
                    _now.AddDays(2).UtcDateTime,
                    [new(alpha, source)],
                    Authority(alpha, "alpha")
                ),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        var features = TestHostFeatureServices.Create(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [],
            clock
        );

        clock.Advance(TimeSpan.FromMinutes(1));
        _ = await features.DisableAsync(alpha, HostFeatureFlags.Collectives, default);
        await UpdateBountyAsync(database, source, 7, clock.GetUtcNow().UtcDateTime);
        await service.BountyChangedAsync(alpha, default);
        _ = (
            await service.InviteAsync(
                new(Guid.NewGuid(), collectiveId, beta, Authority(alpha, "alpha")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.FeatureDisabled>();
        _ = (
            await service.LoadAsync(Authority(alpha, "alpha"), collectiveId, default)
        ).ShouldBeOfType<CollectiveDashboardOutcome.FeatureDisabled>();

        clock.Advance(TimeSpan.FromMinutes(1));
        _ = await features.EnableAsync(alpha, HostFeatureFlags.Collectives, default);
        await service.BountyChangedAsync(alpha, default);
        var retained = (await service.LoadAsync(Authority(alpha, "alpha"), collectiveId, default))
            .ShouldBeOfType<CollectiveDashboardOutcome.Loaded>()
            .Workspace.SelectedCollective!;
        retained.Goal!.Current.ShouldBe(3);

        clock.Advance(TimeSpan.FromMinutes(1));
        await UpdateBountyAsync(database, source, 8, clock.GetUtcNow().UtcDateTime);
        await service.BountyChangedAsync(alpha, default);
        var resumed = (await service.LoadAsync(Authority(alpha, "alpha"), collectiveId, default))
            .ShouldBeOfType<CollectiveDashboardOutcome.Loaded>()
            .Workspace.SelectedCollective!;
        resumed.Goal!.Current.ShouldBe(8);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.Collectives.SingleAsync()).Name.ShouldBe("Cosy Circuit");
        (
            await verify.CollectiveAudits.CountAsync(value =>
                value.Action == CollectiveAuditAction.GoalProgressChanged
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task RaidRelay_RequiresEachHostGateAndDeduplicatesOutOfOrderDomainEvents()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_now);
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var provider = new RecordingRaidProvider();
        var service = new CollectiveService(database, provider, clock);
        var collectiveId = await CreateWithMemberAsync(service, alpha, beta);
        _ = (
            await service.ConfigureRaidRelayAsync(
                new(
                    Guid.NewGuid(),
                    collectiveId,
                    "Weekend relay",
                    alpha,
                    beta,
                    Authority(alpha, "alpha")
                ),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        var loaded = (await service.LoadAsync(Authority(alpha, "alpha"), collectiveId, default))
            .ShouldBeOfType<CollectiveDashboardOutcome.Loaded>()
            .Workspace.SelectedCollective!;
        await SetFeaturesAsync(
            database,
            beta,
            HostFeatureFlags.Collectives | HostFeatureFlags.RaidCollaboration,
            enabled: false
        );

        _ = (
            await service.ConfirmRaidHandoffAsync(
                new(
                    Guid.NewGuid(),
                    collectiveId,
                    loaded.RaidRelay!.Revision,
                    Authority(alpha, "alpha")
                ),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.FeatureDisabled>();
        provider.Started.ShouldBeEmpty();
        await SetFeaturesAsync(
            database,
            beta,
            HostFeatureFlags.Collectives | HostFeatureFlags.RaidCollaboration,
            enabled: true
        );
        var fresh = _now.AddMinutes(2);
        await service.CollaborationEventAsync(
            RaidEvent("relay-event", alpha, beta, fresh, 93),
            default
        );
        await service.CollaborationEventAsync(
            RaidEvent("relay-event", alpha, beta, fresh, 93),
            default
        );
        await service.CollaborationEventAsync(
            RaidEvent("older-event", alpha, beta, fresh.AddMinutes(-1), 30),
            default
        );

        var result = (await service.LoadAsync(Authority(alpha, "alpha"), collectiveId, default))
            .ShouldBeOfType<CollectiveDashboardOutcome.Loaded>()
            .Workspace.SelectedCollective!;
        result.RaidRelay!.CurrentHostLogin.ShouldBe("beta");
        result.RaidRelay.AggregateViewerCount.ShouldBe(93);
        result.RaidRelay.History.Count.ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CollectiveRaidHandoffs.CountAsync()).ShouldBe(1);
        (
            await verify.CollectiveAudits.CountAsync(value =>
                value.Action == CollectiveAuditAction.RaidHandoffConfirmed
            )
        ).ShouldBe(1);
    }

    [Test]
    public Task RaidProviderCompletion_ReconfigurationDoesNotOverwriteNewerRelayState() =>
        AssertProviderIntervalRaceAsync(ProviderIntervalMutation.ReconfigureRelay);

    [Test]
    public Task RaidProviderCompletion_DisabledTargetReturnsTypedNonSuccess() =>
        AssertProviderIntervalRaceAsync(ProviderIntervalMutation.DisableTarget);

    [Test]
    public Task RaidProviderCompletion_ReenabledTargetWatermarkRejectsSuppressedClaim() =>
        AssertProviderIntervalRaceAsync(ProviderIntervalMutation.DisableAndReenableTarget);

    [Test]
    public Task RaidProviderCompletion_RevokedTargetDoesNotOverwriteMembershipState() =>
        AssertProviderIntervalRaceAsync(ProviderIntervalMutation.RevokeTarget);

    [Test]
    public async Task RaidProviderRejection_IsDurableAuditedRevisionedAndIdempotent()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_now);
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var provider = new ControlledRaidProvider();
        var service = new CollectiveService(database, provider, clock);
        var collectiveId = await CreateWithMemberAsync(service, alpha, beta);
        _ = (
            await service.ConfigureRaidRelayAsync(
                new(
                    Guid.NewGuid(),
                    collectiveId,
                    "Weekend relay",
                    alpha,
                    beta,
                    Authority(alpha, "alpha")
                ),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        var before = (await service.LoadAsync(Authority(alpha, "alpha"), collectiveId, default))
            .ShouldBeOfType<CollectiveDashboardOutcome.Loaded>()
            .Workspace.SelectedCollective!;
        var operationId = Guid.NewGuid();
        var command = new ConfirmRaidHandoffCommand(
            operationId,
            collectiveId,
            before.RaidRelay!.Revision,
            Authority(alpha, "alpha")
        );
        var confirmation = service.ConfirmRaidHandoffAsync(command, default);
        await provider.WaitForStartAsync();

        clock.Advance(TimeSpan.FromMinutes(1));
        provider.Complete(new ConfirmedRaidStartOutcome.ProviderRejected());
        var first = (
            await confirmation
        ).ShouldBeOfType<CollectiveMutationOutcome.ProviderRejected>();
        first.WasIdempotent.ShouldBeFalse();
        var second = (
            await service.ConfirmRaidHandoffAsync(command, default)
        ).ShouldBeOfType<CollectiveMutationOutcome.ProviderRejected>();
        second.WasIdempotent.ShouldBeTrue();

        await using var verify = await database.CreateDbContextAsync();
        var relay = await verify
            .CollectiveRaidRelays.Include(value => value.Collective)
                .ThenInclude(value => value.Audits)
            .Include(value => value.Handoffs)
            .SingleAsync();
        relay.Revision.ShouldBe(before.RaidRelay.Revision + 1);
        relay.LastSourceEventAtUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
        relay.UpdatedAtUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
        relay.Collective.Revision.ShouldBe(before.Revision + 1);
        relay.Collective.UpdatedAtUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
        var handoff = relay.Handoffs.ShouldHaveSingleItem();
        handoff.OperationId.ShouldBe(operationId.ToString("N"));
        handoff.Status.ShouldBe(CollectiveRaidHandoffStatus.ProviderRejected);
        var rejectionAudit = relay
            .Collective.Audits.Where(value =>
                value.Action == CollectiveAuditAction.RaidHandoffProviderRejected
            )
            .ShouldHaveSingleItem();
        rejectionAudit.OperationId.ShouldBe($"{operationId:N}:provider-rejected");
        provider.StartCount.ShouldBe(1);
    }

    [Test]
    public async Task PublicProjection_IsAllowlistedAndHidesAllDisabledHostState()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var source = Guid.NewGuid();
        var betaSource = Guid.NewGuid();
        await SeedBountyAsync(database, alpha, source, 4, _now.UtcDateTime);
        await SeedBountyAsync(database, beta, betaSource, 2, _now.UtcDateTime);
        var service = new CollectiveService(
            database,
            new RecordingRaidProvider(),
            new ManualTimeProvider(_now)
        );
        var collectiveId = await CreateWithMemberAsync(service, alpha, beta);
        _ = await service.ConfigureGoalAsync(
            new(
                Guid.NewGuid(),
                collectiveId,
                "Public goal",
                "unit",
                10,
                _now.AddDays(1).UtcDateTime,
                [new(alpha, source)],
                Authority(alpha, "alpha")
            ),
            default
        );
        _ = (
            await service.ConfigureGoalAsync(
                new(
                    Guid.NewGuid(),
                    collectiveId,
                    "Public goal",
                    "unit",
                    10,
                    _now.AddDays(1).UtcDateTime,
                    [new(beta, betaSource)],
                    Authority(alpha, "alpha")
                ),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.AuthorityRequired>();
        _ = (
            await service.SetGoalSourceAsync(
                new(Guid.NewGuid(), collectiveId, betaSource, Authority(beta, "beta")),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();

        var enabled = await service.LoadPublicAsync("alpha", collectiveId, default);
        var enabledProjection = enabled.ShouldNotBeNull();
        enabledProjection.ParticipatingHosts.ShouldContain("alpha");
        enabledProjection.Goal!.Current.ShouldBe(6);
        await SetFeaturesAsync(database, alpha, HostFeatureFlags.Collectives, enabled: false);

        (await service.LoadPublicAsync("alpha", collectiveId, default)).ShouldBeNull();
        var otherHost = await service.LoadPublicAsync("beta", collectiveId, default);
        var otherProjection = otherHost.ShouldNotBeNull();
        otherProjection.ParticipatingHosts.ShouldNotContain("alpha");
        otherProjection.Goal!.Current.ShouldBe(2);
        otherProjection.Goal.HostTotals.ShouldHaveSingleItem().HostLogin.ShouldBe("beta");
    }

    private static async Task<CollectiveId> CreateWithMemberAsync(
        CollectiveService service,
        int coordinatorId,
        int participantId
    )
    {
        var collectiveId = (
            await service.CreateAsync(
                new(Guid.NewGuid(), "Cosy Circuit", Authority(coordinatorId, "alpha")),
                default
            )
        )
            .ShouldBeOfType<CollectiveMutationOutcome.Succeeded>()
            .CollectiveId;
        _ = await service.InviteAsync(
            new(Guid.NewGuid(), collectiveId, participantId, Authority(coordinatorId, "alpha")),
            default
        );
        _ = await service.AcceptInvitationAsync(
            new(Guid.NewGuid(), collectiveId, Authority(participantId, "beta")),
            default
        );
        return collectiveId;
    }

    private static async Task AssertProviderIntervalRaceAsync(ProviderIntervalMutation mutation)
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_now);
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var provider = new ControlledRaidProvider();
        var service = new CollectiveService(database, provider, clock);
        var collectiveId = await CreateWithMemberAsync(service, alpha, beta);
        _ = (
            await service.ConfigureRaidRelayAsync(
                new(
                    Guid.NewGuid(),
                    collectiveId,
                    "Original relay",
                    alpha,
                    beta,
                    Authority(alpha, "alpha")
                ),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        var before = (await service.LoadAsync(Authority(alpha, "alpha"), collectiveId, default))
            .ShouldBeOfType<CollectiveDashboardOutcome.Loaded>()
            .Workspace.SelectedCollective!;
        var confirmation = service.ConfirmRaidHandoffAsync(
            new(
                Guid.NewGuid(),
                collectiveId,
                before.RaidRelay!.Revision,
                Authority(alpha, "alpha")
            ),
            default
        );
        await provider.WaitForStartAsync();

        switch (mutation)
        {
            case ProviderIntervalMutation.ReconfigureRelay:
                _ = (
                    await service.ConfigureRaidRelayAsync(
                        new(
                            Guid.NewGuid(),
                            collectiveId,
                            "Reconfigured relay",
                            alpha,
                            beta,
                            Authority(alpha, "alpha")
                        ),
                        default
                    )
                ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
                break;
            case ProviderIntervalMutation.DisableTarget:
                clock.Advance(TimeSpan.FromMinutes(1));
                _ = await FeatureService(database, clock)
                    .DisableAsync(beta, HostFeatureFlags.RaidCollaboration, default);
                break;
            case ProviderIntervalMutation.DisableAndReenableTarget:
                clock.Advance(TimeSpan.FromMinutes(1));
                var features = FeatureService(database, clock);
                _ = await features.DisableAsync(beta, HostFeatureFlags.Collectives, default);
                clock.Advance(TimeSpan.FromMinutes(1));
                _ = await features.EnableAsync(beta, HostFeatureFlags.Collectives, default);
                break;
            case ProviderIntervalMutation.RevokeTarget:
                _ = (
                    await service.RevokeAsync(
                        new(Guid.NewGuid(), collectiveId, beta, Authority(alpha, "alpha")),
                        default
                    )
                ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }

        provider.Complete(new ConfirmedRaidStartOutcome.Started("beta"));
        var outcome = await confirmation;
        if (mutation == ProviderIntervalMutation.DisableTarget)
        {
            outcome
                .ShouldBeOfType<CollectiveMutationOutcome.FeatureDisabled>()
                .HostId.ShouldBe(beta);
        }
        else
        {
            _ = outcome.ShouldBeOfType<CollectiveMutationOutcome.Conflict>();
        }

        await using var verify = await database.CreateDbContextAsync();
        var relay = await verify
            .CollectiveRaidRelays.Include(value => value.Collective)
                .ThenInclude(value => value.Audits)
            .Include(value => value.Collective)
                .ThenInclude(value => value.Memberships)
            .Include(value => value.Handoffs)
            .SingleAsync();
        relay.CurrentHostId.ShouldBe(alpha);
        relay.NextHostId.ShouldBe(beta);
        relay.Handoffs.ShouldHaveSingleItem().Status.ShouldBe(CollectiveRaidHandoffStatus.Prepared);
        relay.Collective.Audits.ShouldNotContain(value =>
            value.Action == CollectiveAuditAction.RaidHandoffConfirmed
            || value.Action == CollectiveAuditAction.RaidHandoffProviderRejected
        );
        if (mutation == ProviderIntervalMutation.ReconfigureRelay)
        {
            relay.Name.ShouldBe("Reconfigured relay");
            relay.Revision.ShouldBe(before.RaidRelay.Revision + 1);
        }
        if (mutation == ProviderIntervalMutation.RevokeTarget)
        {
            relay
                .Collective.Memberships.Single(value => value.HostId == beta)
                .Status.ShouldBe(CollectiveMembershipStatus.Revoked);
        }
        provider.StartCount.ShouldBe(1);
    }

    private static HostFeatureService FeatureService(
        SqliteBlokeBotDbFactory database,
        TimeProvider clock
    ) =>
        TestHostFeatureServices.Create(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [],
            clock
        );

    private static CollectiveAuthority Authority(int hostId, string login) =>
        new(hostId, $"{login}-id", login, true);

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            EnabledFeatures =
                HostFeatureFlags.Collectives
                | HostFeatureFlags.RaidCollaboration
                | HostFeatureFlags.Competitions
                | HostFeatureFlags.Bounties
                | HostFeatureFlags.Points,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedBountyAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        Guid publicId,
        long total,
        DateTime updatedAtUtc
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.Bounties.Add(
            new Bounty
            {
                HostId = hostId,
                PublicId = publicId,
                CreationOperationId = Guid.NewGuid(),
                CreationFingerprint = Guid.NewGuid().ToString("N"),
                Title = "Source goal",
                Status = BountyStatus.Funding,
                Visibility = BountyVisibility.Public,
                FundingTarget = "12",
                PledgedAmount = total.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ExpiresAtUtc = updatedAtUtc.AddDays(2),
                Revision = 1,
                CreatedAtUtc = updatedAtUtc,
                UpdatedAtUtc = updatedAtUtc,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static async Task UpdateBountyAsync(
        SqliteBlokeBotDbFactory database,
        Guid publicId,
        long total,
        DateTime updatedAtUtc
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var bounty = await db.Bounties.SingleAsync(value => value.PublicId == publicId);
        bounty.PledgedAmount = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        bounty.Revision++;
        bounty.UpdatedAtUtc = updatedAtUtc;
        _ = await db.SaveChangesAsync();
    }

    private static async Task SetFeaturesAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        HostFeatureFlags features,
        bool enabled
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = enabled
            ? host.EnabledFeatures | features
            : host.EnabledFeatures & ~features;
        _ = await db.SaveChangesAsync();
    }

    private static RaidCollaborationDomainEvent RaidEvent(
        string operation,
        int fromHostId,
        int toHostId,
        DateTimeOffset occurredAt,
        int viewers
    ) =>
        new(
            fromHostId,
            RaidCollaborationDomainEventKind.OutgoingRaidRecorded,
            operation,
            RaidDirection.Outgoing,
            $"beta-id",
            "beta",
            "beta",
            viewers,
            null,
            null,
            occurredAt
        );

    private sealed class RecordingRaidProvider : IRaidCollaborationProvider
    {
        internal List<string> Started { get; } = [];

        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
            int hostId,
            string login,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Unavailable()
            );

        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelByIdAsync(
            int hostId,
            string twitchUserId,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Unavailable()
            );

        public Task<FollowedLiveChannelsOutcome> LoadFollowedLiveChannelsAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<FollowedLiveChannelsOutcome>(
                new FollowedLiveChannelsOutcome.Unavailable()
            );

        public Task<bool> HasFollowedLiveAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);

        public Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
            int hostId,
            string targetTwitchUserId,
            string targetLogin,
            CancellationToken cancellationToken
        )
        {
            Started.Add(targetLogin);
            return Task.FromResult<ConfirmedRaidStartOutcome>(
                new ConfirmedRaidStartOutcome.Started(targetLogin)
            );
        }

        public Task<bool> HasRaidManagementAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(true);
    }

    private sealed class ControlledRaidProvider : IRaidCollaborationProvider
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<ConfirmedRaidStartOutcome> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal int StartCount { get; private set; }

        internal Task WaitForStartAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        internal void Complete(ConfirmedRaidStartOutcome outcome) =>
            _ = _completion.TrySetResult(outcome);

        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
            int hostId,
            string login,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Unavailable()
            );

        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelByIdAsync(
            int hostId,
            string twitchUserId,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Unavailable()
            );

        public Task<FollowedLiveChannelsOutcome> LoadFollowedLiveChannelsAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<FollowedLiveChannelsOutcome>(
                new FollowedLiveChannelsOutcome.Unavailable()
            );

        public Task<bool> HasFollowedLiveAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);

        public async Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
            int hostId,
            string targetTwitchUserId,
            string targetLogin,
            CancellationToken cancellationToken
        )
        {
            StartCount++;
            _ = _started.TrySetResult();
            return await _completion.Task.WaitAsync(cancellationToken);
        }

        public Task<bool> HasRaidManagementAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(true);
    }

    private enum ProviderIntervalMutation
    {
        ReconfigureRelay,
        DisableTarget,
        DisableAndReenableTarget,
        RevokeTarget,
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance(TimeSpan duration) => now += duration;
    }
}
