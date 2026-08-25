using System.Data.Common;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostedChannelRuntimeTransitionTests
{
    [Test]
    public async Task StaleChannelStarted_OlderSessionCannotConfirmNewStart()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var harness = Harness(dbFactory);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var firstSession = await harness.TargetAsync(hostId);
        _ = await harness.Transitions.RequestStopAsync(hostId, CancellationToken.None);
        (
            await harness.Lifecycle.MarkStoppedAsync(firstSession, CancellationToken.None)
        ).ShouldBeTrue();
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var secondSession = await harness.TargetAsync(hostId);

        ReferenceEquals(firstSession.SessionIdentity, secondSession.SessionIdentity)
            .ShouldBeFalse();
        (
            await harness.Lifecycle.MarkStartedAsync(firstSession, CancellationToken.None)
        ).ShouldBeFalse();
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Starting
        );

        (
            await harness.Lifecycle.MarkStartedAsync(secondSession, CancellationToken.None)
        ).ShouldBeTrue();
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Started
        );
    }

    [Test]
    public async Task StaleChannelStopped_OlderSessionCannotStopNewSessionAndLaterRestartSucceeds()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var harness = Harness(dbFactory);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var firstSession = await harness.TargetAsync(hostId);
        (
            await harness.Lifecycle.MarkStartedAsync(firstSession, CancellationToken.None)
        ).ShouldBeTrue();
        _ = await harness.Transitions.RequestStopAsync(hostId, CancellationToken.None);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var secondSession = await harness.TargetAsync(hostId);

        (
            await harness.Lifecycle.MarkStoppedAsync(firstSession, CancellationToken.None)
        ).ShouldBeFalse();
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Starting
        );
        (
            await harness.Lifecycle.MarkStartedAsync(secondSession, CancellationToken.None)
        ).ShouldBeTrue();
        _ = await harness.Transitions.RequestStopAsync(hostId, CancellationToken.None);
        (
            await harness.Lifecycle.MarkStoppedAsync(secondSession, CancellationToken.None)
        ).ShouldBeTrue();
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var thirdSession = await harness.TargetAsync(hostId);

        ReferenceEquals(secondSession.SessionIdentity, thirdSession.SessionIdentity)
            .ShouldBeFalse();
        (
            await harness.Lifecycle.MarkStartedAsync(thirdSession, CancellationToken.None)
        ).ShouldBeTrue();
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Started
        );
    }

    [Test]
    public async Task CredentialPolicyStop_InvalidatesCurrentSessionCallback()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var harness = Harness(dbFactory);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var session = await harness.TargetAsync(hostId);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await harness.Transitions.CommitCredentialPolicyStopAsync(
                db,
                hostId,
                CancellationToken.None
            );
        }

        (await harness.Lifecycle.MarkStartedAsync(session, CancellationToken.None)).ShouldBeFalse();
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Stopped
        );
    }

    [Test]
    public async Task ProcessRestart_InvalidatesPriorIdentityAndLeasesFreshStartedSession()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, BotChannelRuntimeState.Started);
        var beforeRestart = Harness(dbFactory);
        var priorSession = await beforeRestart.TargetAsync(hostId);
        var afterRestart = Harness(dbFactory);
        var currentSession = await afterRestart.TargetAsync(hostId);

        ReferenceEquals(priorSession.SessionIdentity, currentSession.SessionIdentity)
            .ShouldBeFalse();
        (
            await afterRestart.Lifecycle.MarkStartedAsync(priorSession, CancellationToken.None)
        ).ShouldBeFalse();
        (
            await afterRestart.Lifecycle.MarkStartedAsync(currentSession, CancellationToken.None)
        ).ShouldBeTrue();
        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Started
        );
    }

    [Test]
    public async Task RepeatedCommandsAndCurrentConfirmations_DoNotCreateNewSessionsOrPublications()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var harness = Harness(dbFactory);

        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var session = await harness.TargetAsync(hostId);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var repeatedSession = await harness.TargetAsync(hostId);
        ReferenceEquals(session.SessionIdentity, repeatedSession.SessionIdentity).ShouldBeTrue();
        (await harness.Lifecycle.MarkStartedAsync(session, CancellationToken.None)).ShouldBeTrue();
        (await harness.Lifecycle.MarkStartedAsync(session, CancellationToken.None)).ShouldBeTrue();
        _ = await harness.Transitions.RequestStopAsync(hostId, CancellationToken.None);
        _ = await harness.Transitions.RequestStopAsync(hostId, CancellationToken.None);
        (await harness.Lifecycle.MarkStoppedAsync(session, CancellationToken.None)).ShouldBeTrue();
        (await harness.Lifecycle.MarkStoppedAsync(session, CancellationToken.None)).ShouldBeFalse();

        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Stopped
        );
        harness.Notifications().ShouldBe(4);
    }

    [Test]
    public async Task InterruptedStopRecovery_CompletesBeforeConcurrentNewStart()
    {
        var pause = new PauseHostUpdateInterceptor();
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync(pause);
        var hostId = await SeedHostAsync(dbFactory, BotChannelRuntimeState.Stopping);
        var harness = Harness(dbFactory);
        pause.Arm();

        var recovery = harness.Lifecycle.RecoverInterruptedStopsAsync(CancellationToken.None);
        await pause.WaitUntilPausedAsync();
        var start = harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        start.IsCompleted.ShouldBeFalse();
        pause.Release();
        await recovery;
        _ = await start;

        (await LoadHostAsync(dbFactory, hostId)).BotRuntimeState.ShouldBe(
            BotChannelRuntimeState.Starting
        );
        harness.Notifications().ShouldBe(2);
    }

    [Test]
    public async Task AuthoritativeTransition_AdminAndSelectedHostProjectionsShowSameLifecycle()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var harness = Harness(dbFactory);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var authorization = ChannelAuthorization(dbFactory, harness.Changes);
        var status = new HostedChannelRuntimeStatusService(
            dbFactory,
            authorization,
            harness.Transitions
        );
        var directory = new HostedChannelDirectoryService(dbFactory);

        var selectedHost = (
            await status.LoadHostRuntimeSummary(hostId).RunAsync(CancellationToken.None)
        ).Match<HostedChannelRuntimeSummary?>(static value => value, static () => null);
        var adminHost = (await directory.LoadHostedChannelsAsync(CancellationToken.None)).Single();

        _ = selectedHost.ShouldNotBeNull();
        _ = selectedHost!.Lifecycle.ShouldBeOfType<HostedChannelRuntimeLifecycle.Starting>();
        _ = adminHost.Lifecycle.ShouldBeOfType<HostedChannelRuntimeLifecycle.Starting>();
    }

    private static TransitionHarness Harness(SqliteBlokeBotDbFactory dbFactory)
    {
        var notifications = 0;
        var events = TestEventBus.Create<AppEventKind>();
        _ = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named($"Test.HostedChannelRuntimeTransition.{Guid.NewGuid():N}"),
            (_, _) =>
            {
                notifications++;
                return ValueTask.CompletedTask;
            }
        );
        var changes = new HostedChannelChangeNotifier(events);
        var transitions = new HostedChannelRuntimeTransitionService(dbFactory, changes);
        return new(
            transitions,
            new HostedChannelRuntimeLifecycleService(transitions),
            changes,
            () => notifications
        );
    }

    private static ChannelBotAuthorizationService ChannelAuthorization(
        SqliteBlokeBotDbFactory dbFactory,
        HostedChannelChangeNotifier changes
    )
    {
        var http = new EmptyHttpClientFactory();
        return new(
            dbFactory,
            changes,
            new ChannelBotOAuthService(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["TwitchBot:ChannelAuthorization:Scopes:0"] = "channel:bot",
                        }
                    )
                    .Build(),
                new OAuthTransport(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
            )
        );
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        BotChannelRuntimeState state = BotChannelRuntimeState.Stopped
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            ChannelBotAuthorizedAtUtc = DateTime.UtcNow,
            ChannelBotAuthorizedScopes = "channel:bot",
            BotRuntimeState = state,
            BotRuntimeStateChangedAtUtc =
                state is BotChannelRuntimeState.Stopped ? null : DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<BotHost> LoadHostAsync(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Hosts.AsNoTracking().SingleAsync(host => host.Id == hostId);
    }

    private sealed record TransitionHarness(
        HostedChannelRuntimeTransitionService Transitions,
        HostedChannelRuntimeLifecycleService Lifecycle,
        HostedChannelChangeNotifier Changes,
        Func<int> Notifications
    )
    {
        internal Task<BotChannelTarget> TargetAsync(int hostId) =>
            Transitions.GetOrCreateSessionTargetAsync(hostId, "streamer", CancellationToken.None);
    }

    private sealed class EmptyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class PauseHostUpdateInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _paused = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _armed;
        private int _intercepted;

        internal void Arm() => _ = Interlocked.Exchange(ref _armed, 1);

        internal async Task WaitUntilPausedAsync() =>
            await _paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        internal void Release() => _released.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Volatile.Read(ref _armed) == 1
                && command.CommandText.Contains("UPDATE \"hosts\"", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0
            )
            {
                _ = _paused.TrySetResult();
                await _released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
