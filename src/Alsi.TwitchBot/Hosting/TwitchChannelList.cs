namespace Alsi.TwitchBot;

internal static class TwitchChannelList
{
    public static string[] Normalize(IEnumerable<string> channels)
    {
        return channels
            .Select(channel => channel.Trim().TrimStart('#').ToLowerInvariant())
            .Where(channel => channel.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(channel => channel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
