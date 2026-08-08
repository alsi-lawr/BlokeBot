namespace BlokeBot.Core.Features;

internal static class CommunityInput
{
    public static string NormalizeLogin(string value) =>
        value.Trim().TrimStart('@').ToLowerInvariant();

    public static bool IsValidLogin(string value) =>
        value.Length is >= 1 and <= 128
        && value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');

    public static string NormalizeSlug(string value) => value.Trim().ToLowerInvariant();

    public static bool IsValidSlug(string value) =>
        value.Length is >= 1 and <= 48
        && value[0] is >= 'a' and <= 'z'
        && value.All(static character =>
            character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'
        );
}
