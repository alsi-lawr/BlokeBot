namespace BlokeBot.Commands;

internal sealed record TwitchCommandRegistrationOptions
{
    public List<Action<ITwitchCommandBuilder>> CommandCallbacks { get; } = [];

    public List<Type> ModuleTypes { get; } = [];
}
