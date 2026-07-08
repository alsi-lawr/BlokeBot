using System.Collections.ObjectModel;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Parses Twitch IRC protocol lines used by the bot runtime.
/// </summary>
public static class TwitchIrcProtocol
{
    private static readonly IReadOnlyDictionary<string, string> EmptyTags = new ReadOnlyDictionary<
        string,
        string
    >(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Determines whether a raw IRC line is a server ping.
    /// </summary>
    /// <param name="line">The raw IRC line.</param>
    /// <returns><see langword="true" /> when the line is a ping.</returns>
    public static bool IsPing(string line) => line.StartsWith("PING ", StringComparison.Ordinal);

    /// <summary>
    /// Creates the matching pong line for a ping line.
    /// </summary>
    /// <param name="line">The raw ping line.</param>
    /// <returns>The pong line to send to the server.</returns>
    public static string CreatePong(string line) =>
        line.Replace("PING", "PONG", StringComparison.Ordinal);

    /// <summary>
    /// Attempts to parse a Twitch private message line.
    /// </summary>
    /// <param name="line">The raw IRC line.</param>
    /// <param name="message">The parsed chat message.</param>
    /// <returns><see langword="true" /> when the line contains a valid private message.</returns>
    public static bool TryParsePrivMsg(string line, out TwitchChatMessage message)
    {
        message = new TwitchChatMessage(string.Empty, string.Empty, string.Empty, line, EmptyTags);

        if (!line.Contains(" PRIVMSG ", StringComparison.Ordinal))
            return false;

        var rest = line;
        IReadOnlyDictionary<string, string> tags = EmptyTags;
        if (rest.StartsWith('@'))
        {
            var tagEnd = rest.IndexOf(' ');
            if (tagEnd <= 1)
                return false;

            tags = ParseTags(rest[1..tagEnd]);
            rest = rest[(tagEnd + 1)..];
        }

        if (!rest.StartsWith(':'))
            return false;

        var prefixEnd = rest.IndexOf(' ');
        if (prefixEnd <= 1)
            return false;

        var prefix = rest[1..prefixEnd];
        var bang = prefix.IndexOf('!');
        if (bang <= 0)
            return false;

        var login = prefix[..bang];
        var commandRest = rest[(prefixEnd + 1)..];
        const string marker = "PRIVMSG #";
        if (!commandRest.StartsWith(marker, StringComparison.Ordinal))
            return false;

        var channelEnd = commandRest.IndexOf(" :", StringComparison.Ordinal);
        if (channelEnd <= marker.Length)
            return false;

        var channel = commandRest[marker.Length..channelEnd];
        var text = commandRest[(channelEnd + 2)..];

        message = new TwitchChatMessage(login, channel, text, line, tags);
        return true;
    }

    private static IReadOnlyDictionary<string, string> ParseTags(string rawTags)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in rawTags.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            if (equals < 0)
            {
                tags[part] = string.Empty;
                continue;
            }

            tags[part[..equals]] = UnescapeTagValue(part[(equals + 1)..]);
        }

        return new ReadOnlyDictionary<string, string>(tags);
    }

    private static string UnescapeTagValue(string value) =>
        value
            .Replace(@"\s", " ", StringComparison.Ordinal)
            .Replace(@"\:", ";", StringComparison.Ordinal)
            .Replace(@"\\", @"\", StringComparison.Ordinal)
            .Replace(@"\r", "\r", StringComparison.Ordinal)
            .Replace(@"\n", "\n", StringComparison.Ordinal);
}
