namespace BlokeBot.Twitch.Runtime;

internal static class BotChannelList
{
    public static string[] Normalize(IEnumerable<string> channels) =>
        channels
            .Select(Login.Normalize)
            .Where(channel => channel.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(channel => channel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
