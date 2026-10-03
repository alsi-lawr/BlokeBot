using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays.Full;

internal static class FullOverlayEditorImportMap
{
    internal static ImportMapDefinition Create(ResourceAssetCollection assets) =>
        ImportMapDefinition.Combine(
            ImportMapDefinition.FromResourceCollection(assets),
            new ImportMapDefinition(
                imports: null,
                scopes: new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["/Features/Overlays/Full/Editor/"] = new Dictionary<string, string>
                    {
                        ["parse5"] = $"/{assets["vendor/full-overlay-editor/parse5/index.js"]}",
                        ["css-tree"] =
                            $"/{assets["vendor/full-overlay-editor/css-tree/csstree.esm.js"]}",
                        ["entities/escape"] =
                            $"/{assets["vendor/full-overlay-editor/entities/escape.js"]}",
                    },
                    ["/vendor/full-overlay-editor/parse5/"] = new Dictionary<string, string>
                    {
                        ["entities/decode"] =
                            $"/{assets["vendor/full-overlay-editor/entities/decode.js"]}",
                        ["entities/escape"] =
                            $"/{assets["vendor/full-overlay-editor/entities/escape.js"]}",
                    },
                },
                integrity: null
            )
        );
}
