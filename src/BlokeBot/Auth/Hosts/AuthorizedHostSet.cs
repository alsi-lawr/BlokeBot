using BlokeBot.Hosts;

namespace BlokeBot.Auth.Hosts;

internal sealed record AuthorizedHostSet(IReadOnlyList<BotHostChoice> Choices, bool CanCreateHost);
