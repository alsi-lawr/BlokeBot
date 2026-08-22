namespace BlokeBot.Plugins.Contracts;

internal static class PluginPackagePath
{
    internal static bool IsValid(string? path) => TryCanonicalize(path, out _);

    internal static bool TryCanonicalize(string? path, out string canonicalPath)
    {
        canonicalPath = string.Empty;
        if (path is null or { Length: < 1 or > 240 } || path[0] == '/' || path[^1] == '/')
        {
            return false;
        }

        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (
                segment is "" or "." or ".."
                || segment.Length > 100
                || (segment[0] == '.' && segment.Length > 1 && segment[1] == '.')
                || segment[^1] is '.' or ' '
                || IsWindowsDeviceSegment(segment)
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

        canonicalPath = string.Join('/', segments);
        return true;
    }

    private static bool IsWindowsDeviceSegment(string segment)
    {
        var name = segment.Split('.', 2)[0];
        var standardDevice =
            name.Equals("con", StringComparison.OrdinalIgnoreCase)
            || name.Equals("prn", StringComparison.OrdinalIgnoreCase)
            || name.Equals("aux", StringComparison.OrdinalIgnoreCase)
            || name.Equals("nul", StringComparison.OrdinalIgnoreCase);
        var numberedDevice =
            name.Length == 4
            && name[3] is >= '1' and <= '9'
            && (
                name.StartsWith("com", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("lpt", StringComparison.OrdinalIgnoreCase)
            );
        return standardDevice || numberedDevice;
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
