using Microsoft.Extensions.DependencyInjection;

namespace Alsi.TwitchBot;

internal readonly record struct TwitchIrcRuntimeStrategy
    : ITwitchBotRuntimeStrategy<TwitchIrcRuntimeStrategy>
{
    public static TwitchBotRuntime Runtime => TwitchBotRuntime.Irc;

    public static Task RunAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<TwitchIrcRuntime>().RunAsync(cancellationToken);
}
