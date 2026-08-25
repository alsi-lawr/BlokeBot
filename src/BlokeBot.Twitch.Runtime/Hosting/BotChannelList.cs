namespace BlokeBot.Twitch.Runtime;

internal static class BotChannelList
{
    public static BotChannelTarget[] Normalize(IEnumerable<BotChannelTarget> targets) =>
        targets
            .Select(static target => new BotChannelTarget(
                Login.Normalize(target.Channel),
                target.SessionIdentity
            ))
            .Where(static target => target.Channel.Length > 0)
            .DistinctBy(static target => target.Channel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static target => target.Channel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
