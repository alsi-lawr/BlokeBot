namespace BlokeBot.Twitch;

public static class QueryString
{
    public static string Create(IEnumerable<KeyValuePair<string, string?>> values) =>
        string.Join(
            '&',
            values
                .Where(static value => !string.IsNullOrWhiteSpace(value.Value))
                .Select(static value =>
                    $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value!)}"
                )
        );
}
