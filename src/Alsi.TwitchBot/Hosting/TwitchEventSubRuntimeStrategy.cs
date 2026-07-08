using Microsoft.Extensions.DependencyInjection;

namespace Alsi.TwitchBot;

internal readonly record struct TwitchEventSubRuntimeStrategy
    : ITwitchBotRuntimeStrategy<TwitchEventSubRuntimeStrategy>
{
    public static TwitchBotRuntime Runtime => TwitchBotRuntime.EventSub;

    public static Task RunAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<TwitchEventSubRuntime>().RunAsync(cancellationToken);
}
