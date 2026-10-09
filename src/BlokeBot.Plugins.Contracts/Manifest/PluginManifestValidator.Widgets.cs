namespace BlokeBot.Plugins.Contracts;

public static partial class PluginManifestValidator
{
    private static void ValidateWidgets(PluginManifest manifest, List<PluginManifestError> errors)
    {
        if (manifest.Widgets.IsDefault)
        {
            errors.Add(new(PluginManifestErrorCode.InvalidWidget, "$.widgets"));
            return;
        }
        ValidateCount(manifest.Widgets, "$.widgets", errors);
        ValidateDistinct(manifest.Widgets.Select(widget => widget.Id), "$.widgets", errors);
        var features = manifest.Features.Select(feature => feature.Id).ToHashSet();
        var modules = manifest.LuaModules.Select(module => module.Id).ToHashSet();
        var assets = manifest
            .Assets.GroupBy(asset => asset.Id)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var widget in manifest.Widgets)
        {
            ValidateName(widget.Title, "$.widgets.title", errors);
            if (
                !features.Contains(widget.FeatureId)
                || !modules.Contains(widget.Module)
                || !PluginHostOperationId.TryCreate(widget.RenderEntryPoint, out _)
                || widget.Assets.IsDefault
                || widget.ConfigurationFields.IsDefault
                || !assets.TryGetValue(widget.DocumentAsset, out var document)
                || document.Kind != PluginAssetKind.Browser
                || !document.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                || widget.Assets.Any(asset => !assets.ContainsKey(asset))
                || widget.ConfigurationFields.Any(field =>
                    string.IsNullOrWhiteSpace(field.Name)
                    || field.Name.Length > PluginContractLimits.MaximumNameCharacters
                    || field.Name.Any(char.IsControl)
                    || string.IsNullOrWhiteSpace(field.Title)
                    || !Enum.IsDefined(field.ValueKind)
                    || field.ValueKind == PluginValueKind.Nil
                    || (field.Portability is { } portability && !Enum.IsDefined(portability))
                )
                || widget
                    .ConfigurationFields.Select(field => field.Name)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != widget.ConfigurationFields.Length
                || !PluginWidgetConfiguration.IsValid(widget, widget.DefaultConfiguration)
            )
            {
                errors.Add(new(PluginManifestErrorCode.InvalidWidget, "$.widgets"));
            }
        }
    }
}
