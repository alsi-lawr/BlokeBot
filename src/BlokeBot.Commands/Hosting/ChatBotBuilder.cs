using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Commands;

internal sealed class ChatBotBuilder(IServiceCollection services) : IChatBotBuilder
{
    public IServiceCollection Services { get; } = services;

    public IChatBotBuilder AddCommands(Action<IChatCommandBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _ = Services.AddSingleton(new ChatCommandRegistration { Configure = configure });
        return this;
    }

    public IChatBotBuilder AddCommandModule<TModule>()
        where TModule : class, IChatCommandModule
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IChatCommandModule, TModule>());
        return this;
    }

    public IChatBotBuilder AddCommandFilter<TFilter>()
        where TFilter : class, IChatCommandFilter
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IChatCommandFilter, TFilter>());
        return this;
    }
}
