using BlokeBot.Simulation.FakeTwitch;
using BlokeBot.Twitch.Runtime;

namespace BlokeBot.Simulation;

internal sealed class SimulationReadiness(
    FakeTwitchAuthority authority,
    IBotRuntimeStatusAccessor runtime,
    IEventSubChannelStatusAccessor channels
)
{
    private int _persistenceReady;

    public void MarkPersistenceReady()
    {
        Interlocked.Exchange(ref _persistenceReady, 1);
    }

    public SimulationReadinessProjection Project()
    {
        var transcript = authority.Transcript;
        var exchanges = transcript.Where(entry => entry.Kind == "oauth.exchange").ToArray();
        var providerReady =
            exchanges.Any(entry => entry.Detail == authority.Definition.BotUser.Login)
            && exchanges.Count(entry => entry.Detail == authority.Definition.AuthorizedUser.Login)
                >= 3
            && transcript.Count(entry => entry.Kind == "oauth.validate") >= 2;
        var subscriptionsReady = authority.ActiveSubscriptions.Count == 12;
        var runtimeReady =
            runtime.Current is BotRuntimeStatus.Connected
            && channels.Current.Channels.Any(status => status is EventSubChannelStatus.Healthy);
        var initialEventsReady =
            transcript.Any(entry =>
                entry.Kind == "eventsub.deliver" && entry.Detail == "channel.chat.message"
            )
            && transcript.Any(entry =>
                entry.Kind == "helix.chat.message"
                && entry.Detail.Contains("@nightowl", StringComparison.Ordinal)
            );
        var eventSubReady = subscriptionsReady && runtimeReady;
        var ready =
            Volatile.Read(ref _persistenceReady) == 1
            && providerReady
            && eventSubReady
            && initialEventsReady;

        return new SimulationReadinessProjection(
            authority.Definition.Name,
            Volatile.Read(ref _persistenceReady) == 1,
            providerReady,
            eventSubReady,
            initialEventsReady,
            ready
        );
    }
}

internal sealed record SimulationReadinessProjection(
    string Scenario,
    bool Persistence,
    bool OAuthAndProvider,
    bool EventSub,
    bool InitialEvents,
    bool Ready
);
