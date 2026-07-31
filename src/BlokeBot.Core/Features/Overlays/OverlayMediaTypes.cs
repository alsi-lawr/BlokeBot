namespace BlokeBot.Core.Features.Overlays;

internal static class OverlayMediaTypes
{
    public const string AcceptedBrowserMedia = "image/*,audio/*,video/*";

    public static OverlayCueMediaKind? Kind(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return OverlayCueMediaKind.Image;
        }

        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return OverlayCueMediaKind.Audio;
        }

        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return OverlayCueMediaKind.Video;
        }

        return null;
    }

    public static string? NormalizeDeclaration(string? contentType)
    {
        if (
            string.IsNullOrWhiteSpace(contentType)
            || !System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        )
        {
            return null;
        }

        var normalized = parsed.MediaType?.ToLowerInvariant();
        if (
            normalized is null
            || normalized.EndsWith("/*", StringComparison.Ordinal)
            || Kind(normalized) is null
        )
        {
            return null;
        }

        return normalized;
    }

    public static string Label(string contentType) =>
        Kind(contentType) switch
        {
            OverlayCueMediaKind.Image => "Image",
            OverlayCueMediaKind.Audio => "Audio",
            OverlayCueMediaKind.Video => "Video",
            _ => "Unsupported",
        };
}
