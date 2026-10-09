using BlokeBot.Core.Components;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayToolbox
{
    [Inject]
    private FullOverlayWidgetRegistry _registry { get; set; } = default!;

    [Inject]
    private PluginWidgetCatalog _plugins { get; set; } = default!;

    [Parameter]
    public int HostId { get; set; }

    [Parameter]
    public EventCallback<FullOverlayWidgetKind> Add { get; set; }

    [Parameter]
    public EventCallback AddOrdinary { get; set; }

    [Parameter]
    public string Feedback { get; set; } = string.Empty;

    [Parameter]
    public EventCallback Close { get; set; }

    private EditorToolbox? _presentation;
    private string _search = string.Empty;

    internal ValueTask FocusSearchAsync() => _presentation!.FocusSearchAsync();

    private bool Matches(string value) =>
        value.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool OrdinaryMatches() => Matches("Text / HTML element ordinary editable source");

    private IEnumerable<FullOverlayWidgetDescriptor> Items() =>
        _registry
            .Descriptors.Where(item => Matches($"{item.Name} {Purpose(item)} {item.Kind.Value}"))
            .OrderBy(item => item.Name);

    private (PluginId Plugin, PluginWidgetDescriptor Descriptor)? Plugin(FullOverlayWidgetKind kind)
    {
        foreach (var declaration in _plugins.Declarations)
        {
            if (
                $"plugin:{declaration.Plugin.Value}/{declaration.Descriptor.Id.Value}" == kind.Value
            )
            {
                return declaration;
            }
        }
        return null;
    }

    private bool Ready((PluginId Plugin, PluginWidgetDescriptor Descriptor) declaration) =>
        PluginHostId.TryCreate(HostId, out var host)
        && _plugins.Resolve(declaration.Plugin, declaration.Descriptor.Id, host) is not null;

    private string PluginStatus((PluginId Plugin, PluginWidgetDescriptor Descriptor) declaration) =>
        Ready(declaration)
            ? $"Plugin {declaration.Plugin.Value} · ready for this channel"
            : $"Plugin {declaration.Plugin.Value} · not ready for this channel; draft can still be added";

    private string Purpose(FullOverlayWidgetDescriptor item) =>
        item.Kind.Value switch
        {
            "guessing" => "Show the channel's guessing game.",
            "cue-player" => "Play overlay cues using the existing cue player.",
            "giveaway" => "Show the channel's giveaway.",
            "viewer-queue" => "Show current and upcoming viewers from a queue.",
            "community-goal" => "Show progress towards a community goal.",
            "viewer-funded-bounty" => "Show the channel's viewer-funded bounty.",
            "event-feed" => "Show events from an overlay feed.",
            "html" => "Keep this widget's HTML and CSS isolated from the document.",
            "web" => "Embed a sandboxed web page.",
            "image" => "Show an uploaded image.",
            "audio" => "Play uploaded audio.",
            "video" => "Play an uploaded video.",
            _ => Plugin(item.Kind) is { } plugin
                ? $"Plugin-declared widget from {plugin.Plugin.Value}."
                : $"Add {item.Name} to this overlay.",
        };
}
