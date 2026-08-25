using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidShoutoutOutcomeAuthorityTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 25, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RestartedCallbacks_DoNotDowngradeDeliveredAndPinFailureRemainsTerminal()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var identity = await SeedQueuedAsync(database, "restart-ordering");
        await using (var firstProcess = await database.CreateDbContextAsync())
        {
            _ = (
                await new AutomaticRaidShoutoutOutcomeAuthority().ApplyAsync(
                    firstProcess,
                    identity,
                    new AutomaticRaidOutcomeTransition.TransportDelivered(),
                    _now.AddSeconds(1),
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomaticRaidOutcomeTransitionResult.Applied>();
        }

        await using (var restartedProcess = await database.CreateDbContextAsync())
        {
            var authority = new AutomaticRaidShoutoutOutcomeAuthority();
            _ = (
                await authority.ApplyAsync(
                    restartedProcess,
                    identity,
                    new AutomaticRaidOutcomeTransition.QueueAccepted(),
                    _now.AddSeconds(2),
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomaticRaidOutcomeTransitionResult.Unchanged>();
            _ = (
                await authority.ApplyAsync(
                    restartedProcess,
                    identity,
                    new AutomaticRaidOutcomeTransition.TerminalFailure(
                        AutomaticRaidShoutoutResultCode.Rejected
                    ),
                    _now.AddSeconds(3),
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomaticRaidOutcomeTransitionResult.Unchanged>();
        }

        await AssertStateAsync(
            database,
            AutomaticRaidShoutoutOutcomeStatus.Delivered,
            AutomaticRaidShoutoutResultCode.Delivered,
            RaidShoutoutOutcome.Sent
        );

        await using (var pinProcess = await database.CreateDbContextAsync())
        {
            var authority = new AutomaticRaidShoutoutOutcomeAuthority();
            _ = (
                await authority.ApplyAsync(
                    pinProcess,
                    identity,
                    new AutomaticRaidOutcomeTransition.PinFailed(),
                    _now.AddSeconds(4),
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomaticRaidOutcomeTransitionResult.Applied>();
            _ = (
                await authority.ApplyAsync(
                    pinProcess,
                    identity,
                    new AutomaticRaidOutcomeTransition.PinFailed(),
                    _now.AddSeconds(5),
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomaticRaidOutcomeTransitionResult.Unchanged>();
            _ = (
                await authority.ApplyAsync(
                    pinProcess,
                    identity,
                    new AutomaticRaidOutcomeTransition.TransportDelivered(),
                    _now.AddSeconds(6),
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomaticRaidOutcomeTransitionResult.Unchanged>();
        }

        await AssertStateAsync(
            database,
            AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
            AutomaticRaidShoutoutResultCode.PartialFailure,
            RaidShoutoutOutcome.Sent
        );
    }

    [Test]
    public async Task ConcurrentTransportAndTerminalCallbacks_CommitExactlyOneCoherentOutcome()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var identity = await SeedQueuedAsync(database, "concurrent-callbacks");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remainingReady = 2;
        var delivered = ApplyAsync(new AutomaticRaidOutcomeTransition.TransportDelivered());
        var failed = ApplyAsync(
            new AutomaticRaidOutcomeTransition.TerminalFailure(
                AutomaticRaidShoutoutResultCode.Rejected
            )
        );
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        start.SetResult();

        var results = await Task.WhenAll(delivered, failed);

        results.Count(result => result is AutomaticRaidOutcomeTransitionResult.Applied).ShouldBe(1);
        results
            .Count(result => result is AutomaticRaidOutcomeTransitionResult.Unchanged)
            .ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        var outcome = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync();
        var history = await verify.RaidCollaborationHistory.SingleAsync();
        if (outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Delivered)
        {
            outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Delivered);
            history.ShoutoutOutcome.ShouldBe(RaidShoutoutOutcome.Sent);
        }
        else
        {
            outcome.Status.ShouldBe(AutomaticRaidShoutoutOutcomeStatus.NotDelivered);
            outcome.ResultCode.ShouldBe(AutomaticRaidShoutoutResultCode.Rejected);
            history.ShoutoutOutcome.ShouldBe(RaidShoutoutOutcome.Rejected);
        }

        async Task<AutomaticRaidOutcomeTransitionResult> ApplyAsync(
            AutomaticRaidOutcomeTransition transition
        )
        {
            if (Interlocked.Decrement(ref remainingReady) == 0)
            {
                ready.SetResult();
            }
            await start.Task;
            await using var db = await database.CreateDbContextAsync();
            return await new AutomaticRaidShoutoutOutcomeAuthority().ApplyAsync(
                db,
                identity,
                transition,
                _now.AddSeconds(1),
                CancellationToken.None
            );
        }
    }

    private static async Task<AutomaticRaidOutcomeIdentity> SeedQueuedAsync(
        SqliteBlokeBotDbFactory database,
        string providerMessageId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.Hosts.Add(
            new BotHost
            {
                Id = 1,
                TwitchUserId = "host-id",
                Login = "host",
                DisplayName = "Host",
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = _now.UtcDateTime,
            }
        );
        var outcome = new AutomaticRaidShoutoutOutcome
        {
            HostId = 1,
            ProviderMessageId = providerMessageId,
            SourceTwitchUserId = "raider-id",
            SourceLogin = "raider",
            SourceDisplayName = "Raider",
            ViewerCount = 10,
            Status = AutomaticRaidShoutoutOutcomeStatus.Queued,
            ResultCode = AutomaticRaidShoutoutResultCode.Queued,
            MessageTimestampUtc = _now.UtcDateTime,
            ClaimedAtUtc = _now.UtcDateTime,
        };
        _ = db.AutomaticRaidShoutoutOutcomes.Add(outcome);
        _ = db.RaidCollaborationHistory.Add(
            new RaidCollaborationHistoryEntry
            {
                HostId = 1,
                ProviderMessageId = providerMessageId,
                Direction = RaidDirection.Incoming,
                OtherTwitchUserId = "raider-id",
                OtherLogin = "raider",
                OtherDisplayName = "Raider",
                ViewerCount = 10,
                OccurredAtUtc = _now.UtcDateTime,
                WelcomeOutcome = RaidWelcomeOutcome.NotConfigured,
                ShoutoutOutcome = RaidShoutoutOutcome.Queued,
                RecordedAtUtc = _now.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
        return new AutomaticRaidOutcomeIdentity(1, outcome.Id, providerMessageId);
    }

    private static async Task AssertStateAsync(
        SqliteBlokeBotDbFactory database,
        AutomaticRaidShoutoutOutcomeStatus status,
        AutomaticRaidShoutoutResultCode resultCode,
        RaidShoutoutOutcome historyOutcome
    )
    {
        await using var verify = await database.CreateDbContextAsync();
        var outcome = await verify.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(status);
        outcome.ResultCode.ShouldBe(resultCode);
        (await verify.RaidCollaborationHistory.SingleAsync()).ShoutoutOutcome.ShouldBe(
            historyOutcome
        );
    }
}
