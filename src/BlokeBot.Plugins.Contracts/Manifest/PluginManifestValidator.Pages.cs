namespace BlokeBot.Plugins.Contracts;

public static partial class PluginManifestValidator
{
    private static void ValidatePages(PluginManifest manifest, List<PluginManifestError> errors)
    {
        ValidateCount(manifest.GeneratedPages, "$.generatedPages", errors);
        ValidateCount(manifest.EmbeddedPages, "$.embeddedPages", errors);
        var allPageIds = manifest
            .GeneratedPages.Select(page => page.Id)
            .Concat(manifest.EmbeddedPages.Select(page => page.Id));
        ValidateDistinct(allPageIds, "$.pages", errors);

        var featureIds = manifest.Features.Select(feature => feature.Id).ToHashSet();
        var moduleIds = manifest.LuaModules.Select(module => module.Id).ToHashSet();
        var assets = manifest
            .Assets.GroupBy(asset => asset.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in manifest.GeneratedPages)
        {
            ValidateName(page.Title, "$.generatedPages.title", errors);
            if (
                !featureIds.Contains(page.FeatureId)
                || !moduleIds.Contains(page.Module)
                || !PluginHostOperationId.TryCreate(page.RenderEntryPoint, out _)
                || !ValidRoute(page.Route)
                || !routes.Add(page.Route)
            )
            {
                errors.Add(new(PluginManifestErrorCode.InvalidPage, "$.generatedPages"));
            }
        }

        foreach (var page in manifest.EmbeddedPages)
        {
            ValidateName(page.Title, "$.embeddedPages.title", errors);
            var validDocument =
                assets.TryGetValue(page.DocumentAsset, out var document)
                && document.Kind == PluginAssetKind.Browser
                && document.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase);
            var validAssets = page.Assets.All(assets.ContainsKey);
            if (
                !featureIds.Contains(page.FeatureId)
                || !validDocument
                || !validAssets
                || !ValidRoute(page.Route)
                || !routes.Add(page.Route)
                || page.MessageOrigins.Length > PluginContractLimits.MaximumDeclarationsPerSurface
                || page.MessageOrigins.Any(origin => !ValidHttpsOrigin(origin))
            )
            {
                errors.Add(new(PluginManifestErrorCode.InvalidPage, "$.embeddedPages"));
            }
        }
    }

    private static bool ValidRoute(string? route) =>
        route is { Length: >= 1 and <= 80 }
        && route[0] is >= 'a' and <= 'z'
        && route[^1] is (>= 'a' and <= 'z') or (>= '0' and <= '9')
        && route.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static bool ValidHttpsOrigin(Uri? origin) =>
        origin is { IsAbsoluteUri: true }
        && origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(origin.UserInfo)
        && origin.AbsolutePath == "/"
        && string.IsNullOrEmpty(origin.Query)
        && string.IsNullOrEmpty(origin.Fragment);
}
