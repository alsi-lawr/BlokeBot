using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayInspector
{
    [Parameter, EditorRequired]
    public required FullOverlayEditorView View { get; set; }

    [Parameter]
    public EventCallback<object> Command { get; set; }

    [Parameter]
    public EventCallback<(string Axis, int Edge)> Align { get; set; }

    [Parameter]
    public EventCallback Find { get; set; }

    [Parameter]
    public EventCallback Replay { get; set; }

    [Parameter, EditorRequired]
    public required Func<object, Task<FullOverlayHierarchyFeedback>> HierarchyPreview { get; set; }

    private bool _hierarchyOpen;
    private bool _hierarchyPending;
    private bool _focusDestination;
    private string _relation = "inside";
    private string? _destination;
    private ElementReference _destinationElement;
    private FullOverlayHierarchyFeedback _hierarchyFeedback = new(false, "Choose a destination.");
    private FullOverlayEditorView? _hierarchyView;
    private long _hierarchyRequest;

    protected override void OnParametersSet()
    {
        if (
            _hierarchyView is { } prior
            && prior.Revision == View.Revision
            && prior.SelectionVersion == View.SelectionVersion
            && prior.Selected == View.Selected
            && prior.Focus == View.Focus
            && prior.Members.SequenceEqual(View.Members)
        )
        {
            return;
        }
        _hierarchyView = View;
        _hierarchyRequest++;
        _hierarchyPending = _hierarchyOpen;
        _hierarchyFeedback = new(false, "Checking the current destination…");
        if (!View.Layers.Any(layer => layer.Key == _destination))
        {
            _destination = View.Layers.FirstOrDefault(layer => layer.Key != View.Selected)?.Key;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_hierarchyPending)
        {
            _hierarchyPending = false;
            await RefreshHierarchyAsync();
            await InvokeAsync(StateHasChanged);
        }
        if (_focusDestination)
        {
            _focusDestination = false;
            await _destinationElement.FocusAsync();
        }
    }

    public async Task OpenHierarchyAsync()
    {
        _hierarchyOpen = true;
        _hierarchyPending = true;
        _focusDestination = true;
        await InvokeAsync(StateHasChanged);
    }

    private async Task HierarchyOpenAsync(bool open)
    {
        _hierarchyOpen = open;
        if (open)
        {
            await RefreshHierarchyAsync();
        }
    }

    private async Task RelationAsync(string relation)
    {
        _relation = relation;
        await RefreshHierarchyAsync();
    }

    private async Task DestinationAsync(ChangeEventArgs args)
    {
        _destination = args.Value?.ToString();
        await RefreshHierarchyAsync();
    }

    private async Task RefreshHierarchyAsync()
    {
        _hierarchyFeedback = new(false, "Checking the current destination…");
        var request = ++_hierarchyRequest;
        var result = await HierarchyPreview(new { relation = _relation, target = _destination });
        if (request == _hierarchyRequest)
        {
            _hierarchyFeedback = result;
        }
    }

    private Task MoveHierarchyAsync() =>
        Send(
            new
            {
                kind = "hierarchy",
                relation = _relation,
                target = _destination,
            }
        );

    private string PlacementStatus()
    {
        var position = View.NativeStyles.GetValueOrDefault("position");
        if (position is null)
        {
            return "Measuring placement…";
        }
        if (position is "absolute" or "fixed")
        {
            return $"{char.ToUpperInvariant(position[0]) + position[1..]} positioning · outside normal flow";
        }
        var translate = View.NativeStyles.GetValueOrDefault("translate", "none");
        return translate == "none"
            ? "Normal layout · no visual offset"
            : $"Layout slot kept · offset {translate}";
    }

    private bool _effectsOpen;
    private bool _positionOpen;
    private bool _motionOpen;
    private bool _styleOpen = true;
    private bool _audioOpen;
    private string _motionPhase = "entrance";
    private string _duration = "300";
    private string _easing = "ease-out";
    private static readonly (string Property, string Label)[] _textFields =
    [
        ("font-family", "Font"),
        ("font-size", "Text size"),
        ("font-weight", "Weight"),
    ];
    private static readonly (string Property, string Label)[] _effectFields =
    [
        ("padding", "Padding"),
        ("gap", "Gap"),
        ("border-radius", "Corners"),
        ("box-shadow", "Shadow"),
        ("opacity", "Opacity"),
        ("display", "Layout"),
    ];

    private string Value(string property, string fallback = "") =>
        View.Styles.GetValueOrDefault(property, fallback);

    private Task Send(object command) => Command.InvokeAsync(command);

    private Task StyleAsync(string property, string value) =>
        Send(
            new
            {
                kind = "style",
                property,
                value,
            }
        );

    private string Position(string axis) =>
        Value(
            $"--blokebot-{axis}",
            Value(
                axis == "x" ? "left" : "top",
                axis == "x" ? View.Widget?.Authoring.X ?? "0px" : View.Widget?.Authoring.Y ?? "0px"
            )
        );

    private int Anchor(string axis) =>
        int.TryParse(Value($"--blokebot-anchor-{axis}"), out var value)
            ? value
            : (int)(
                axis == "x"
                    ? View.Widget?.Authoring.HorizontalAnchor ?? FullOverlayAnchor.Start
                    : View.Widget?.Authoring.VerticalAnchor ?? FullOverlayAnchor.Start
            );

    private Task PositionAsync(string axis, string value) =>
        Send(
            new
            {
                kind = "position",
                axis,
                value,
            }
        );

    private Task AnchorAsync(string axis, ChangeEventArgs args) =>
        Send(
            new
            {
                kind = "anchor",
                axis,
                value = int.Parse(args.Value!.ToString()!, CultureInfo.InvariantCulture),
            }
        );

    private Task DimensionAsync(string property, string value) =>
        Send(
            new
            {
                kind = "layout",
                properties = new Dictionary<string, string> { [property] = value },
                metadata = new Dictionary<string, string> { [property] = value },
            }
        );

    private Task VolumeAsync(ChangeEventArgs args) =>
        Send(
            new
            {
                kind = "audio",
                property = "volume",
                value = double.Parse(args.Value!.ToString()!, CultureInfo.InvariantCulture),
            }
        );

    private Task MotionAsync(FullOverlayMotionPreset preset) =>
        double.TryParse(
            _duration,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var duration
        )
        && double.IsFinite(duration)
        && duration >= 0
            ? Send(
                new
                {
                    kind = "motion",
                    phase = _motionPhase,
                    preset = preset.ToString().ToLowerInvariant(),
                    duration,
                    easing = _easing,
                }
            )
            : Task.CompletedTask;
}
