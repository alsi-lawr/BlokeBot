namespace BlokeBot.Commands;

internal sealed class ChatCommandRegistry
{
    public ChatCommandRegistry(
        IEnumerable<ChatCommandRegistration> registrations,
        IEnumerable<IChatCommandModule> modules,
        IEnumerable<IChatCommandFilter> filters
    )
    {
        var builder = new ChatCommandPlanBuilder(filters);

        foreach (var registration in registrations)
        {
            registration.Configure(builder);
        }

        foreach (var module in modules)
        {
            module.AddCommands(builder);
        }

        Plan = builder.Build();
    }

    public ChatCommandPlan Plan { get; }
}
