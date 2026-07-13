using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace BlokeBot.Twitch.Runtime;

internal sealed class BotRuntimeHostedService : BackgroundService
{
    private readonly BotSettings _settings;
    private readonly IrcRuntime _irc;
    private readonly EventSubRuntime _eventSub;

    public BotRuntimeHostedService(BotSettings settings, IrcRuntime irc, EventSubRuntime eventSub)
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
            ChatRuntime.Irc => _irc.RunAsync(cancellationToken),
            ChatRuntime.EventSub => _eventSub.RunAsync(cancellationToken),
            _ => throw new UnreachableException("Unknown validated Twitch bot runtime."),
        };
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return RunSelectedRuntimeAsync(stoppingToken);
    }
}
