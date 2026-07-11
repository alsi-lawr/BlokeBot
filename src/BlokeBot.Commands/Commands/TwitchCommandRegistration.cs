namespace BlokeBot.Commands;

internal sealed record TwitchCommandRegistration
{
    public required Action<ITwitchCommandBuilder> Configure { get; init; }
}
