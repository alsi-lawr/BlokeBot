namespace BlokeBot.Commands;

internal sealed record ChatCommandRegistration
{
    public required Action<IChatCommandBuilder> Configure { get; init; }
}
