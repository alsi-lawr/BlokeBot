namespace BlokeBot.Core.Hosts;

public sealed record BotHostSelection(
    BotHostChoice Current,
    IReadOnlyList<BotHostChoice> Available
);
