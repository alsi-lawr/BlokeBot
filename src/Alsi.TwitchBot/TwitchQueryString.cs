namespace Alsi.TwitchBot;

internal static class TwitchQueryString
{
    public static string Create(IEnumerable<KeyValuePair<string, string?>> values) =>
        string.Join(
            '&',
            values
                .Where(value => !string.IsNullOrWhiteSpace(value.Value))
                .Select(value =>
                    $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value!)}"
                )
        );
}
