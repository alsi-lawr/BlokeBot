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
    public async Task StartCompletion_OperatorStopWinsWhenCompletionAlreadyObservedGeneration()
    {
        var pause = new PauseTransitionInterceptor();
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync(pause);
        var hostId = await SeedHostAsync(dbFactory);
        var harness = Harness(dbFactory);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        pause.Arm();

        var lateStart = harness.Lifecycle.MarkStartedAsync("streamer", CancellationToken.None);
        await pause.WaitUntilPausedAsync();
        _ = await harness.Transitions.RequestStopAsync(hostId, CancellationToken.None);
        pause.Release();
        await lateStart;

        var host = await LoadHostAsync(dbFactory, hostId);
        host.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Stopping);
        host.BotRuntimeGeneration.ShouldBe(2);
        harness.Notifications().ShouldBe(2);
    }

    [Test]
    public async Task StartCompletion_CredentialPolicyStopWinsWhenCompletionAlreadyObservedGeneration()
    {
        var pause = new PauseTransitionInterceptor();
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync(pause);
        var hostId = await SeedHostAsync(dbFactory);
        var harness = Harness(dbFactory);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        pause.Arm();

        var lateStart = harness.Lifecycle.MarkStartedAsync("streamer", CancellationToken.None);
        await pause.WaitUntilPausedAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = await harness.Transitions.ForceStoppedForCredentialPolicyAsync(
                db,
                hostId,
                CancellationToken.None
            );
            _ = await harness.Changes.NotifyChangedAsync(CancellationToken.None);
        }
        pause.Release();
        await lateStart;

        var host = await LoadHostAsync(dbFactory, hostId);
        host.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Stopped);
        host.BotRuntimeGeneration.ShouldBe(2);
        harness.Notifications().ShouldBe(2);
    }

    [Test]
    public async Task RepeatedCommandsAndConfirmations_DoNotCreateNewOperationsOrPublications()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var harness = Harness(dbFactory);

        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        await harness.Lifecycle.MarkStartedAsync("streamer", CancellationToken.None);
        await harness.Lifecycle.MarkStartedAsync("streamer", CancellationToken.None);
        _ = await harness.Transitions.RequestStopAsync(hostId, CancellationToken.None);
        _ = await harness.Transitions.RequestStopAsync(hostId, CancellationToken.None);
        await harness.Lifecycle.MarkStoppedAsync("streamer", CancellationToken.None);
        await harness.Lifecycle.MarkStoppedAsync("streamer", CancellationToken.None);

        var host = await LoadHostAsync(dbFactory, hostId);
        host.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Stopped);
        host.BotRuntimeGeneration.ShouldBe(2);
        harness.Notifications().ShouldBe(4);
    }

    [Test]
    public async Task InterruptedStopRecovery_NewerStartWinsAfterRecoveryCapturedGeneration()
    {
        var pause = new PauseTransitionInterceptor();
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync(pause);
        var hostId = await SeedHostAsync(dbFactory, BotChannelRuntimeState.Stopping, generation: 7);
        var harness = Harness(dbFactory);
        pause.Arm();

        var recovery = harness.Lifecycle.RecoverInterruptedStopsAsync(CancellationToken.None);
        await pause.WaitUntilPausedAsync();
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        pause.Release();
        await recovery;

        var host = await LoadHostAsync(dbFactory, hostId);
        host.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Starting);
        host.BotRuntimeGeneration.ShouldBe(8);
        harness.Notifications().ShouldBe(1);
    }

    [Test]
    public async Task AuthoritativeTransition_AdminAndSelectedHostProjectionsShowSameLifecycle()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var harness = Harness(dbFactory);
        _ = await harness.Transitions.RequestStartAsync(hostId, CancellationToken.None);
        var authorization = ChannelAuthorization(dbFactory, harness.Changes);
        var status = new HostedChannelRuntimeStatusService(dbFactory, authorization);
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
        BotChannelRuntimeState state = BotChannelRuntimeState.Stopped,
        long generation = 0
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
            BotRuntimeGeneration = generation,
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
    );

    private sealed class EmptyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class PauseTransitionInterceptor : DbCommandInterceptor
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
                && IsGenerationFencedHostUpdate(command)
                && Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0
            )
            {
                _ = _paused.TrySetResult();
                await _released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        private static bool IsGenerationFencedHostUpdate(DbCommand command) =>
            command.CommandText.Contains("UPDATE \"hosts\"", StringComparison.Ordinal)
            && command.CommandText.Contains("BotRuntimeGeneration", StringComparison.Ordinal);
    }
}
