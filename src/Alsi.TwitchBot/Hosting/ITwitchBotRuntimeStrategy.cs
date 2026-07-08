using Microsoft.Extensions.DependencyInjection;

namespace Alsi.TwitchBot;

internal interface ITwitchBotRuntimeStrategy<TStrategy>
    where TStrategy : ITwitchBotRuntimeStrategy<TStrategy>
{
    static abstract TwitchBotRuntime Runtime { get; }

    static abstract Task RunAsync(IServiceProvider services, CancellationToken cancellationToken);

    static virtual bool Matches(TwitchBotRuntime runtime) => runtime == TStrategy.Runtime;
}
