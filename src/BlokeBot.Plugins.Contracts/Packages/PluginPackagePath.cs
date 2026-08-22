namespace BlokeBot.Plugins.Contracts;

internal static class PluginPackagePath
{
    internal static bool IsValid(string? path)
    {
        if (path is null or { Length: < 1 or > 240 } || path[0] == '/' || path[^1] == '/')
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (
                segment is "" or "." or ".."
                || segment.Length > 100
                || (segment[0] == '.' && segment.Length > 1 && segment[1] == '.')
            )
            {
                return false;
            }

            foreach (var character in segment)
            {
                if (!IsSafeCharacter(character))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsSafeCharacter(char character) =>
        character
            is (>= 'a' and <= 'z')
                or (>= 'A' and <= 'Z')
                or (>= '0' and <= '9')
                or '.'
                or '-'
                or '_';
}
