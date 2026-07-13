using System.Collections.ObjectModel;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Parses Twitch IRC protocol lines used by the bot runtime.
/// </summary>
public static class TwitchIrcProtocol
{
    private static readonly IReadOnlyDictionary<string, string> _emptyTags = new ReadOnlyDictionary<
        string,
        string
    >(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Determines whether a raw IRC line is a server ping.
    /// </summary>
    /// <param name="line">The raw IRC line.</param>
    /// <returns><see langword="true" /> when the line is a ping.</returns>
    public static bool IsPing(string line)
    {
        return line.StartsWith("PING ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates the matching pong line for a ping line.
    /// </summary>
    /// <param name="line">The raw ping line.</param>
    /// <returns>The pong line to send to the server.</returns>
    public static string CreatePong(string line)
    {
        return line.Replace("PING", "PONG", StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses a Twitch private message line and returns the exact parser outcome.
    /// </summary>
    /// <param name="line">The raw IRC line.</param>
    /// <returns>The typed private message parse result.</returns>
    public static TwitchIrcPrivMsgParseResult ParsePrivMsg(string line)
    {
        if (!line.Contains(" PRIVMSG ", StringComparison.Ordinal))
        {
            return Failure(TwitchIrcPrivMsgParseStatus.NotPrivMsg, line);
        }

        var rest = line;
        IReadOnlyDictionary<string, string> tags = _emptyTags;
        if (rest.StartsWith('@'))
        {
            var tagEnd = rest.IndexOf(' ');
            if (tagEnd <= 1)
            {
                return Failure(TwitchIrcPrivMsgParseStatus.MissingTagTerminator, line);
            }

            tags = ParseTags(rest[1..tagEnd]);
            rest = rest[(tagEnd + 1)..];
        }

        if (!rest.StartsWith(':'))
        {
            return Failure(TwitchIrcPrivMsgParseStatus.MissingPrefix, line);
        }

        var prefixEnd = rest.IndexOf(' ');
        if (prefixEnd <= 1)
        {
            return Failure(TwitchIrcPrivMsgParseStatus.MalformedPrefix, line);
        }

        var prefix = rest[1..prefixEnd];
        var bang = prefix.IndexOf('!');
        if (bang <= 0)
        {
            return Failure(TwitchIrcPrivMsgParseStatus.MissingUserLogin, line);
        }

        var login = prefix[..bang];
        var commandRest = rest[(prefixEnd + 1)..];
        const string marker = "PRIVMSG #";
        if (!commandRest.StartsWith(marker, StringComparison.Ordinal))
        {
            return Failure(TwitchIrcPrivMsgParseStatus.MalformedCommand, line);
        }

        var channelEnd = commandRest.IndexOf(" :", StringComparison.Ordinal);
        if (channelEnd <= marker.Length)
        {
            return Failure(TwitchIrcPrivMsgParseStatus.MissingChannelOrText, line);
        }

        var channel = commandRest[marker.Length..channelEnd];
        var text = commandRest[(channelEnd + 2)..];
        var message = new TwitchChatMessage(login, channel, text, line, tags);

        return new TwitchIrcPrivMsgParseResult(TwitchIrcPrivMsgParseStatus.Parsed, message);
    }

    private static TwitchIrcPrivMsgParseResult Failure(
        TwitchIrcPrivMsgParseStatus status,
        string line
    )
    {
        return new(
            status,
            new TwitchChatMessage(string.Empty, string.Empty, string.Empty, line, _emptyTags)
        );
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

    private static string UnescapeTagValue(string value)
    {
        return value
            .Replace(@"\s", " ", StringComparison.Ordinal)
            .Replace(@"\:", ";", StringComparison.Ordinal)
            .Replace(@"\\", @"\", StringComparison.Ordinal)
            .Replace(@"\r", "\r", StringComparison.Ordinal)
            .Replace(@"\n", "\n", StringComparison.Ordinal);
    }
}
