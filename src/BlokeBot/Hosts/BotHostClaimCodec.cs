namespace BlokeBot.Hosts;

internal static class BotHostClaimCodec
{
    private const char Separator = '|';

    public static string Encode(BotHostChoice host) =>
        string.Join(
            Separator,
            host.Id.ToString(),
            Escape(host.Login),
            Escape(host.DisplayName),
            Escape(host.Role),
            Escape(host.ProfileImageUrl ?? string.Empty)
        );

    public static BotHostChoice? Decode(string value)
    {
        var parts = value.Split(Separator);
        return parts.Length is 4 or 5 && int.TryParse(parts[0], out var id)
            ? new BotHostChoice(
                id,
                Unescape(parts[1]),
                Unescape(parts[2]),
                Unescape(parts[3]),
                parts.Length == 5 ? Unescape(parts[4]) : null
            )
            : null;
    }

    private static string Escape(string value) =>
        value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace(Separator.ToString(), "%7C", StringComparison.Ordinal);

    private static string Unescape(string value) =>
        value
            .Replace("%7C", Separator.ToString(), StringComparison.Ordinal)
            .Replace("%25", "%", StringComparison.Ordinal);
}
