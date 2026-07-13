namespace BlokeBot.Commands;

internal sealed class TwitchCommandRegistry
{
    public TwitchCommandRegistry(
        TwitchCommandRegistrationSnapshot registrations,
        IEnumerable<ITwitchCommandModule> modules,
        IEnumerable<ITwitchCommandFilter> filters
    )
    {
        var builder = new TwitchCommandPlanBuilder(filters);

        foreach (var callback in registrations.CommandCallbacks)
        {
            callback(builder);
        }

        foreach (var module in modules)
        {
            module.AddCommands(builder);
        }

        Plan = builder.Build();
    }

    public TwitchCommandPlan Plan { get; }
}
