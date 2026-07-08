using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BlokeBot.Commands;

internal sealed class TwitchCommandRegistry(
    IOptions<TwitchCommandRegistrationOptions> registrations
)
{
    public TwitchCommandPlan Build(IServiceProvider services)
    {
        var builder = new TwitchCommandPlanBuilder();

        foreach (var callback in registrations.Value.CommandCallbacks)
            callback(builder);

        foreach (var moduleType in registrations.Value.ModuleTypes)
        {
            var module = (ITwitchCommandModule)
                ActivatorUtilities.CreateInstance(services, moduleType);
            module.AddCommands(builder);
        }

        return builder.Build();
    }
}
