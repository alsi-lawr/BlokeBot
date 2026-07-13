using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchBotRuntimeHostedService : BackgroundService
{
    private readonly TwitchBotSettings _settings;
    private readonly TwitchIrcRuntime _irc;
    private readonly TwitchEventSubRuntime _eventSub;

    public TwitchBotRuntimeHostedService(
        TwitchBotSettings settings,
        TwitchIrcRuntime irc,
        TwitchEventSubRuntime eventSub
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(irc);
        ArgumentNullException.ThrowIfNull(eventSub);

        _settings = settings;
        _irc = irc;
        _eventSub = eventSub;
    }

    internal Task RunSelectedRuntimeAsync(CancellationToken cancellationToken)
    {
        return _settings.Runtime switch
        {
            TwitchBotRuntime.Irc => _irc.RunAsync(cancellationToken),
            TwitchBotRuntime.EventSub => _eventSub.RunAsync(cancellationToken),
            _ => throw new UnreachableException("Unknown validated Twitch bot runtime."),
        };
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return RunSelectedRuntimeAsync(stoppingToken);
    }
}
