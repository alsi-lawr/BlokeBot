namespace BlokeBot.Twitch.Runtime;

internal static class PublicChatMessageSplitter
{
    public static IReadOnlyList<string> Split(string message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return [];
        }

        if (maxLength <= 0)
        {
            return [message.Trim()];
        }

        var remaining = message.Trim();
        var parts = new List<string>();
        while (remaining.Length > maxLength)
        {
            var breakIndex = FindBreakIndex(remaining, maxLength);
            var part = remaining[..breakIndex].Trim();
            if (!string.IsNullOrWhiteSpace(part))
            {
                parts.Add(part);
            }

            remaining = remaining[breakIndex..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            parts.Add(remaining);
        }

        return parts;
    }

    private static int FindBreakIndex(string value, int maxLength)
    {
        var searchLength = Math.Min(value.Length, maxLength);
        var segment = value.AsSpan(0, searchLength);
        var lineBreak = segment.LastIndexOfAny('\n', '\r');
        if (lineBreak > 0)
        {
            return lineBreak;
        }

        var sentenceBreak = LastSentenceBreak(segment);
        if (sentenceBreak > 0)
        {
            return sentenceBreak;
        }

        var wordBreak = LastWordBreak(segment);
        return wordBreak > 0 ? wordBreak : searchLength;
    }

    private static int LastSentenceBreak(ReadOnlySpan<char> value)
    {
        for (var i = value.Length - 1; i > 0; i--)
        {
            if (!char.IsWhiteSpace(value[i]))
            {
                continue;
            }

            var punctuation = value[i - 1];
            if (punctuation is '.' or '!' or '?')
            {
                return i;
            }
        }

        return -1;
    }

    private static int LastWordBreak(ReadOnlySpan<char> value)
    {
        for (var i = value.Length - 1; i > 0; i--)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
