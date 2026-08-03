namespace BlokeBot.Core.Features.Overlays;

internal static class OverlayMediaTypes
{
    public const string AcceptedBrowserMedia = "image/*,audio/*,video/*";

    public static OverlayCueMediaKind? Kind(string contentType) =>
        contentType switch
        {
            var value when value.StartsWith("image/", StringComparison.OrdinalIgnoreCase) =>
                OverlayCueMediaKind.Image,
            var value when value.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) =>
                OverlayCueMediaKind.Audio,
            var value when value.StartsWith("video/", StringComparison.OrdinalIgnoreCase) =>
                OverlayCueMediaKind.Video,
            _ => null,
        };

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
        return
            normalized is null
            || normalized.EndsWith("/*", StringComparison.Ordinal)
            || Kind(normalized) is null
            ? null
            : normalized;
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
