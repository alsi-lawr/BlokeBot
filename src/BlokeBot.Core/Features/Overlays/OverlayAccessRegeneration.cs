namespace BlokeBot.Core.Features.Overlays;

internal static class OverlayAccessRegeneration
{
    internal const string AlertSource = "configuration-import";
    internal const string AlertSourceKey = "overlay-browser-source-regeneration";
    internal const string FollowUpCode = "overlay-browser-source-regeneration-required";
    internal const string LinkPath = "/overlays";
    internal const string Title = "Browser Source URLs need regeneration";
    internal const string Instructions =
        "Open Overlays, select each source marked 'URL regeneration required', select Generate private URL, copy the one-time URL, and replace that source's URL in OBS. If Generate private URL is unavailable, turn the source's required tool on in Channel setup first.";

    internal static string Message(IReadOnlyList<string> sourceNames) =>
        $"{sourceNames.Count} imported {SourceLabel(sourceNames.Count)} cannot connect until a new private URL is generated: {string.Join(", ", sourceNames)}. {Instructions}";

    private static string SourceLabel(int count) =>
        count == 1 ? "Browser Source" : "Browser Sources";
}
