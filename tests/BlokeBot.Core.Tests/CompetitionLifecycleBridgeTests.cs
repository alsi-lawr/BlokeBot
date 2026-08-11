using BlokeBot.Core.Features.Competitions;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CompetitionLifecycleBridgeTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task EnabledDisabledAndReenabled_BridgesOnlyFreshAuthorisedOccurrences()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.Competitions | HostFeatureFlags.Automations,
                CreatedAtUtc = _now.UtcDateTime,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
        }
        var events = TestEventBus.Create<AppEventKind>();
        var applicationEvents = 0;
        _ = events.Subscribe(
            AppEventKind.CompetitionsChanged,
            ObserverIdentity.Named("Test.Competitions.Lifecycle"),
            (_, _) =>
            {
                applicationEvents++;
                return ValueTask.CompletedTask;
            }
        );
        var automations = new RecordingAutomationDispatcher();
        var bridge = new CompetitionLifecycleBridge(
            database,
            events,
            automations,
            NullLogger<CompetitionLifecycleBridge>.Instance
        );
        var competitionId = new CompetitionId(Guid.NewGuid());

        await bridge.CompetitionChangedAsync(
            Event(hostId, competitionId, _now),
            CancellationToken.None
        );
        applicationEvents.ShouldBe(1);
        automations.Events.Count.ShouldBe(1);

        await using (var disable = await database.CreateDbContextAsync())
        {
            var host = await disable.Hosts.SingleAsync(x => x.Id == hostId);
            host.EnabledFeatures &= ~HostFeatureFlags.Competitions;
            _ = await disable.SaveChangesAsync();
        }
        await bridge.CompetitionChangedAsync(
            Event(hostId, competitionId, _now.AddSeconds(1)),
            CancellationToken.None
        );
        applicationEvents.ShouldBe(1);
        automations.Events.Count.ShouldBe(1);

        await using (var enable = await database.CreateDbContextAsync())
        {
            var host = await enable.Hosts.SingleAsync(x => x.Id == hostId);
            host.EnabledFeatures |= HostFeatureFlags.Competitions;
            host.CompetitionsAcceptWorkAfterUtc = _now.AddSeconds(3).UtcDateTime;
            _ = await enable.SaveChangesAsync();
        }
        automations.Events.Count.ShouldBe(1);
        await bridge.CompetitionChangedAsync(
            Event(hostId, competitionId, _now.AddSeconds(2)),
            CancellationToken.None
        );
        automations.Events.Count.ShouldBe(1);
        await bridge.CompetitionChangedAsync(
            Event(hostId, competitionId, _now.AddSeconds(3)),
            CancellationToken.None
        );
        applicationEvents.ShouldBe(2);
        automations.Events.Count.ShouldBe(2);
        automations.Events.ShouldAllBe(x => x.PublicPayload == "{\"status\":\"Running\"}");
    }

    private static CompetitionLifecycleEvent Event(
        int hostId,
        CompetitionId competitionId,
        DateTimeOffset occurredAt
    ) =>
        new(
            Guid.NewGuid(),
            hostId,
            competitionId,
            CompetitionEventKind.Started,
            "{\"status\":\"Running\"}",
            occurredAt
        );

    private sealed class RecordingAutomationDispatcher : ICompetitionLifecycleAutomationDispatcher
    {
        public List<CompetitionLifecycleEvent> Events { get; } = [];

        public Task DispatchAsync(
            CompetitionLifecycleEvent competitionEvent,
            BotHost host,
            CancellationToken cancellationToken
        )
        {
            Events.Add(competitionEvent);
            return Task.CompletedTask;
        }
    }
}
