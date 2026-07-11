using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Commands;

internal sealed class TwitchBotBuilder(IServiceCollection services) : ITwitchBotBuilder
{
    public IServiceCollection Services { get; } = services;

    public ITwitchBotBuilder AddCommands(Action<ITwitchCommandBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure<TwitchCommandRegistrationOptions>(options =>
            options.CommandCallbacks.Add(configure)
        );
        return this;
    }

    public ITwitchBotBuilder AddCommandModule<TModule>()
        where TModule : class, ITwitchCommandModule
    {
        Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITwitchCommandModule, TModule>()
        );
        return this;
    }

    public ITwitchBotBuilder AddCommandFilter<TFilter>()
        where TFilter : class, ITwitchCommandFilter
    {
        Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITwitchCommandFilter, TFilter>()
        );
        return this;
    }
}
