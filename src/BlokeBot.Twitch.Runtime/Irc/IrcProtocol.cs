using System.Collections.ObjectModel;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Parses Twitch IRC protocol lines used by the bot runtime.
/// </summary>
public static class IrcProtocol
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
    public static IrcPrivMsgParseOutcome ParsePrivMsg(string line)
    {
        if (!line.Contains(" PRIVMSG ", StringComparison.Ordinal))
        {
            return new IrcPrivMsgParseOutcome.NotPrivMsg();
        }

        var rest = line;
        IReadOnlyDictionary<string, string> tags = _emptyTags;
        if (rest.StartsWith('@'))
        {
            var tagEnd = rest.IndexOf(' ');
            if (tagEnd <= 1)
            {
                return new IrcPrivMsgParseOutcome.MissingTagTerminator();
            }

            tags = ParseTags(rest[1..tagEnd]);
            rest = rest[(tagEnd + 1)..];
        }

        if (!rest.StartsWith(':'))
        {
            return new IrcPrivMsgParseOutcome.MissingPrefix();
        }

        var prefixEnd = rest.IndexOf(' ');
        if (prefixEnd <= 1)
        {
            return new IrcPrivMsgParseOutcome.MalformedPrefix();
        }

        var prefix = rest[1..prefixEnd];
        var bang = prefix.IndexOf('!');
        if (bang <= 0)
        {
            return new IrcPrivMsgParseOutcome.MissingUserLogin();
        }

        var login = prefix[..bang];
        var commandRest = rest[(prefixEnd + 1)..];
        const string Marker = "PRIVMSG #";
        if (!commandRest.StartsWith(Marker, StringComparison.Ordinal))
        {
            return new IrcPrivMsgParseOutcome.MalformedCommand();
        }

        var channelEnd = commandRest.IndexOf(" :", StringComparison.Ordinal);
        if (channelEnd <= Marker.Length)
        {
            return new IrcPrivMsgParseOutcome.MissingChannelOrText();
        }

        var channel = commandRest[Marker.Length..channelEnd];
        var text = commandRest[(channelEnd + 2)..];
        var message = new ChatMessage(login, channel, text, line, tags);

        return new IrcPrivMsgParseOutcome.Parsed(message);
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
