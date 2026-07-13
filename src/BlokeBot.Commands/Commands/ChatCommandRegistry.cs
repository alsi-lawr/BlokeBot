namespace BlokeBot.Commands;

internal sealed class ChatCommandRegistry
{
    public ChatCommandRegistry(
        ChatCommandRegistrationSnapshot registrations,
        IEnumerable<IChatCommandModule> modules,
        IEnumerable<IChatCommandFilter> filters
    )
    {
        var builder = new ChatCommandPlanBuilder(filters);

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

    public ChatCommandPlan Plan { get; }
}
