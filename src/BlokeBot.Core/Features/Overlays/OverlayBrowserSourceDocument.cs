using System.Net;

namespace BlokeBot.Core.Features.Overlays;

internal enum OverlayBrowserSourceCredentials
{
    Omit,
    SameOrigin,
}

internal static class OverlayBrowserSourceDocument
{
    internal static string Render(
        PathString pathBase,
        string statePath,
        string livePath,
        OverlayBrowserSourceCredentials credentials,
        bool liveEnabled
    )
    {
        var root = statePath.EndsWith("/state", StringComparison.Ordinal)
            ? statePath[..^"/state".Length]
            : statePath;
        return Render(
            pathBase,
            statePath,
            livePath,
            $"{root}/media",
            $"{root}/cue-complete",
            $"{root}/appearance.css",
            credentials,
            liveEnabled
        );
    }

    internal static string Render(
        PathString pathBase,
        string statePath,
        string livePath,
        string mediaPath,
        string completionPath,
        string appearanceStylePath,
        OverlayBrowserSourceCredentials credentials,
        bool liveEnabled
    )
    {
        var prefix = pathBase.HasValue ? pathBase.Value : string.Empty;
        var encodedPrefix = WebUtility.HtmlEncode(prefix);
        var encodedStateUrl = WebUtility.HtmlEncode($"{prefix}{statePath}");
        var encodedLiveUrl = WebUtility.HtmlEncode($"{prefix}{livePath}");
        var encodedMediaUrl = WebUtility.HtmlEncode($"{prefix}{mediaPath}");
        var encodedCompletionUrl = WebUtility.HtmlEncode($"{prefix}{completionPath}");
        var encodedAppearanceStyleUrl = WebUtility.HtmlEncode($"{prefix}{appearanceStylePath}");
        var credentialsValue =
            credentials is OverlayBrowserSourceCredentials.SameOrigin ? "same-origin" : "omit";
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
              <meta name="robots" content="noindex,nofollow,noarchive">
              <title>BlokeBot overlay</title>
              <link rel="stylesheet" href="{{encodedPrefix}}/overlay/assets/blokebot-overlay.css">
              <link id="overlay-appearance-style" rel="stylesheet" href="{{encodedAppearanceStyleUrl}}">
            </head>
            <body>
              <main id="overlay-root" data-state-url="{{encodedStateUrl}}" data-live-url="{{encodedLiveUrl}}" data-media-url="{{encodedMediaUrl}}" data-completion-url="{{encodedCompletionUrl}}" data-credentials="{{credentialsValue}}" data-live-enabled="{{liveEnabled.ToString().ToLowerInvariant()}}" data-status="loading" aria-live="off">
                <svg id="overlay-canvas" viewBox="0 0 1920 1080" preserveAspectRatio="xMidYMid meet" aria-hidden="true" xmlns="http://www.w3.org/2000/svg"></svg>
                <div id="cue-canvas" aria-hidden="true"></div>
              </main>
              <script src="{{encodedPrefix}}/overlay/assets/blokebot-overlay.js" defer></script>
            </body>
            </html>
            """;
    }
}
