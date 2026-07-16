namespace BlokeBot.Core.Hosts;

internal sealed record AuthorizedHostSet(IReadOnlyList<BotHostChoice> Choices, bool CanCreateHost);
