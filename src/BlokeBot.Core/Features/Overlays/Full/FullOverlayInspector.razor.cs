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
    private bool _motionOpen;
    private bool _styleOpen = true;
    private bool _audioOpen;
    private string _motionPhase = "entrance";
    private string _motionPreset = "fade";
    private string _duration = "300";
    private string _easing = "ease-out";
    private static readonly (string Property, string Label)[] _styleFields =
    [
        ("color", "Text colour"),
        ("background", "Background"),
        ("font-family", "Font"),
        ("font-size", "Text size"),
        ("font-weight", "Weight"),
        ("text-align", "Text align"),
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

    private Task RotateAsync(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)
        && double.IsFinite(degrees)
            ? Send(
                new
                {
                    kind = "layout",
                    properties = new { rotate = $"{value}deg" },
                    metadata = new { rotationDegrees = degrees },
                }
            )
            : Task.CompletedTask;

    private Task ScaleAsync(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
        && double.IsFinite(scale)
            ? Send(
                new
                {
                    kind = "layout",
                    properties = new { scale = value },
                    metadata = new { scaleX = scale, scaleY = scale },
                }
            )
            : Task.CompletedTask;

    private Task VolumeAsync(ChangeEventArgs args) =>
        Send(
            new
            {
                kind = "audio",
                property = "volume",
                value = double.Parse(args.Value!.ToString()!, CultureInfo.InvariantCulture),
            }
        );

    private Task MotionAsync() =>
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
                    preset = _motionPreset,
                    duration,
                    easing = _easing,
                }
            )
            : Task.CompletedTask;
}
