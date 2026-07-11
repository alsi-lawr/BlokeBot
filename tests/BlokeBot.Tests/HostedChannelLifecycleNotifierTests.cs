using BlokeBot.BotRuntime;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class HostedChannelLifecycleNotifierTests
{
    [Test]
    public async Task HostedChannel_ReceivingLifecycleNotifications_PersistsEachTransition()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedStartingHostAsync(dbFactory);
        var events = TestEventBus.Create<AppEventKind>();
        var changeCount = 0;
        events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.HostedChannelLifecycleNotifier"),
            (_, _) =>
            {
                changeCount++;
                return ValueTask.CompletedTask;
            }
        );
        var notifier = new HostedChannelLifecycleNotifier(
            new HostedChannelRuntimeLifecycleService(
                dbFactory,
                new HostedChannelChangeNotifier(events)
            )
        );

        await notifier.ChannelStartedAsync("Streamer", CancellationToken.None);

        (await LoadRuntimeStateAsync(dbFactory)).ShouldBe(BotChannelRuntimeState.Started);
        changeCount.ShouldBe(1);

        await notifier.ChannelStoppedAsync("streamer", CancellationToken.None);

        (await LoadRuntimeStateAsync(dbFactory)).ShouldBe(BotChannelRuntimeState.Stopped);
        changeCount.ShouldBe(2);
    }

    private static async Task SeedStartingHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Hosts.Add(
            new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                BotRuntimeState = BotChannelRuntimeState.Starting,
                BotRuntimeStateChangedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<BotChannelRuntimeState> LoadRuntimeStateAsync(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Hosts.Select(host => host.BotRuntimeState).SingleAsync();
    }
}
