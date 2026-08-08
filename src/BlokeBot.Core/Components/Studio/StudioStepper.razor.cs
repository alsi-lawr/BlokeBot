using System.Globalization;
using System.Numerics;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// A whole-number control carrying its unit and its step. The value stays a string so the caller
/// keeps its own parsing and save-time validation, and so magnitudes past <see cref="long"/>
/// survive typing. Stepping clamps to <see cref="Minimum"/> and <see cref="Maximum"/>; typing does
/// not, so an out-of-range entry still reaches the validator that already reports it. Two steppers
/// become a range by binding each one's bound to the other's value, which needs no paired variant.
/// </summary>
public partial class StudioStepper
{
    [Parameter, EditorRequired]
    public required string Id { get; set; }

    [Parameter, EditorRequired]
    public required string AriaLabel { get; set; }

    [Parameter]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    [Parameter]
    public BigInteger Step { get; set; } = BigInteger.One;

    [Parameter]
    public BigInteger? Minimum { get; set; }

    [Parameter]
    public BigInteger? Maximum { get; set; }

    [Parameter]
    public string? Unit { get; set; }

    [Parameter]
    public string DecrementLabel { get; set; } = "Less";

    [Parameter]
    public string IncrementLabel { get; set; } = "More";

    private Task OnInput(ChangeEventArgs args) =>
        ValueChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty);

    private Task StepAsync(int direction)
    {
        var stepped = Current() + (Step * direction);
        if (Minimum is { } minimum && stepped < minimum)
        {
            stepped = minimum;
        }

        if (Maximum is { } maximum && stepped > maximum)
        {
            stepped = maximum;
        }

        return ValueChanged.InvokeAsync(stepped.ToString(CultureInfo.InvariantCulture));
    }

    private BigInteger Current() =>
        BigInteger.TryParse(
            Value.Trim(),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : Minimum ?? BigInteger.Zero;
}
