namespace BlokeBot.Core.Features.Plugins;

internal static class MarketplacePackagePath
{
    internal static bool IsCanonical(string? path)
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
                || segment[^1] is '.' or ' '
                || segment.Any(character =>
                    character
                        is not (
                            (>= 'a' and <= 'z')
                            or (>= 'A' and <= 'Z')
                            or (>= '0' and <= '9')
                            or '.'
                            or '-'
                            or '_'
                        )
                )
            )
            {
                return false;
            }
        }

        return true;
    }
}
