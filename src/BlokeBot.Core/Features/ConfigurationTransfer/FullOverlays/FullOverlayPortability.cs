using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;

internal sealed class FullOverlayPortability(IPluginFeatureDeclarationProvider declarations)
{
    internal PluginFeatureDeclarationSnapshot Capture() => declarations.Current;

    internal bool IsCurrent(PluginFeatureDeclarationSnapshot snapshot) =>
        ReferenceEquals(snapshot, declarations.Current);

    internal FullOverlayDocument Map(
        FullOverlayDocument document,
        PluginFeatureDeclarationSnapshot snapshot,
        IReadOnlyDictionary<Guid, Guid>? media = null
    ) =>
        document with
        {
            Diagnostics = [],
            Widgets = [.. document.Widgets.Select(widget => Map(widget, snapshot, media))],
        };

    private static FullOverlayWidget Map(
        FullOverlayWidget widget,
        PluginFeatureDeclarationSnapshot snapshot,
        IReadOnlyDictionary<Guid, Guid>? media
    )
    {
        JsonElement configuration;
        var setup = widget.RequiresSetup;
        switch (widget.Kind.Value)
        {
            case "image" or "audio" or "video":
                var asset = FullOverlayWidgetConfigurations.Read<FullOverlayMediaConfiguration>(
                    widget.Configuration
                );
                configuration = Json(
                    asset is null
                        ? new FullOverlayMediaConfiguration(Guid.Empty, false)
                        : asset with
                        {
                            AssetId = media is null
                                ? asset.AssetId
                                : media.GetValueOrDefault(asset.AssetId),
                        }
                );
                setup |= asset is null || (media is not null && !media.ContainsKey(asset.AssetId));
                break;
            case "html":
                configuration = Json(
                    FullOverlayWidgetConfigurations.Read<FullOverlayHtmlConfiguration>(
                        widget.Configuration
                    ) ?? new FullOverlayHtmlConfiguration("", "")
                );
                break;
            case "web":
                configuration = Json(
                    FullOverlayWidgetConfigurations.Read<FullOverlayWebConfiguration>(
                        widget.Configuration
                    ) ?? new FullOverlayWebConfiguration("")
                );
                break;
            case "event-feed":
                // Binding groups remain intact, but no feed queue or source-host state transfers.
                configuration = FullOverlayEventFeeds.TryConfiguration(
                    widget with
                    {
                        RequiresSetup = false,
                    },
                    out var feed
                )
                    ? Json(
                        new { feed.BindingId, Feed = JsonNode.Parse(feed.Feed.ToPersistenceJson()) }
                    )
                    : Empty();
                setup = true;
                break;
            case "guessing"
            or "giveaway"
            or "cue-player"
            or "viewer-queue"
            or "community-goal"
            or "viewer-funded-bounty":
                configuration = Native(widget);
                setup = true;
                break;
            default:
                configuration = Plugin(widget, snapshot);
                setup = true;
                break;
        }
        return widget with { Configuration = configuration, RequiresSetup = setup };
    }

    private static JsonElement Native(FullOverlayWidget widget)
    {
        var type = widget.Kind.Value switch
        {
            "guessing" => OverlayType.Guessing,
            "giveaway" => OverlayType.Giveaway,
            "cue-player" => OverlayType.CuePlayer,
            "viewer-queue" => OverlayType.ViewerQueue,
            "community-goal" => OverlayType.CommunityGoal,
            "viewer-funded-bounty" => OverlayType.ViewerFundedBounty,
            _ => throw new InvalidOperationException("Unknown native source."),
        };
        if (type == OverlayType.ViewerQueue)
        {
            try
            {
                var dto = widget.Configuration.Deserialize<ViewerQueueConfigurationDto>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        UnmappedMemberHandling = System
                            .Text
                            .Json
                            .Serialization
                            .JsonUnmappedMemberHandling
                            .Disallow,
                    }
                );
                return dto is null ? Empty() : Json(dto with { QueueId = 0 });
            }
            catch (JsonException)
            {
                return Empty();
            }
        }
        if (
            OverlayConfiguration.Parse(type, widget.Configuration.GetRawText())
            is not OverlayConfigurationParseResult.Valid parsed
        )
        {
            return Empty();
        }
        var value = JsonNode.Parse(parsed.Value.ToPersistenceJson())!.AsObject();
        if (type is OverlayType.CommunityGoal or OverlayType.ViewerFundedBounty)
        {
            value["selectedItemId"] = null;
        }
        return Json(value);
    }

    private static JsonElement Plugin(
        FullOverlayWidget widget,
        PluginFeatureDeclarationSnapshot snapshot
    )
    {
        var descriptor = snapshot
            .Declarations.Values.SelectMany(declaration =>
                declaration.Manifest.Widgets.Select(item =>
                    (
                        Kind: $"plugin:{declaration.Installation.PluginId.Value}/{item.Id.Value}",
                        Item: item
                    )
                )
            )
            .FirstOrDefault(item => item.Kind == widget.Kind.Value)
            .Item;
        return
            descriptor is null
            || FullOverlayPluginValues.FromJson(widget.Configuration) is not PluginValue.Map map
            || PluginValueValidator.Validate(map) is not PluginValueValidationOutcome.Valid
            ? Empty()
            : FullOverlayPluginValues.ToJson(
                new PluginValue.Map([
                    .. map.Properties.Where(property =>
                        descriptor.ConfigurationFields.Any(field =>
                            field.Name == property.Name
                            && field.ValueKind == property.Value.Kind
                            && field.Portability == PluginWidgetFieldPortability.Portable
                        )
                    ),
                ])
            );
    }

    private static JsonElement Empty() => Json(new Dictionary<string, string>());

    private static JsonElement Json<T>(T value) =>
        JsonSerializer.SerializeToElement(
            value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
}
