using System.Globalization;
using System.Numerics;
using BlokeBot.Core.Components.Studio;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Shouldly;

namespace BlokeBot.Core.Tests;

/// <summary>
/// The stepper is the shared control BLOKEBOT-165 extracts and BLOKEBOT-166 consumes. What is not
/// obvious from reading it is the asymmetry it deliberately keeps: stepping clamps, typing does
/// not, so a value outside the allowed range still reaches the surface's own validator instead of
/// being silently rewritten.
/// </summary>
public sealed class StudioStepperTests
{
    private static readonly BigInteger _pointCeiling = BigInteger.Pow(10, 100);

    [Test]
    public void Stepping_MovesByTheStepAndClampsAtBothBounds()
    {
        Press("300", "increment").ShouldBe("330");
        Press("300", "decrement").ShouldBe("270");
        Press("1790", "increment").ShouldBe("1800");
        Press("1800", "increment").ShouldBe("1800");
        Press("40", "decrement").ShouldBe("30");
        Press("30", "decrement").ShouldBe("30");
    }

    [Test]
    public void TypedValue_OutsideTheRange_ReachesTheCallerUntouched()
    {
        using var context = new BunitContext();
        var value = "300";
        var stepper = Render(context, value, next => value = next);

        stepper.Find("input").Input("5000");

        value.ShouldBe("5000");
    }

    [Test]
    public void UnparseableValue_Stepping_ResumesFromTheFloor()
    {
        Press("not a number", "increment").ShouldBe("60");
        Press(string.Empty, "decrement").ShouldBe("30");
    }

    [Test]
    public void Stepping_NearTheHugestAllowedAmount_KeepsEveryDigit()
    {
        var ceiling = _pointCeiling.ToString(CultureInfo.InvariantCulture);

        PressReward((_pointCeiling - 10).ToString(CultureInfo.InvariantCulture), "increment")
            .ShouldBe(ceiling);
        PressReward(ceiling, "increment").ShouldBe(ceiling);
        PressReward(ceiling, "decrement")
            .ShouldBe((_pointCeiling - 10).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Points pairs a smallest and a largest prize stepping by ten, and the pairing is nothing but
    /// each stepper taking the sibling's value as its own bound, so the control needs no paired
    /// variant. What is not obvious from reading one stepper is that the bound is live: raising the
    /// largest lets the smallest climb past where it was just clamped, and lowering the smallest
    /// lets the largest fall past where it was just clamped.
    /// </summary>
    [Test]
    public void PairedRange_BoundToTheSibling_ClampsEachSideAtTheOthersLiveValue()
    {
        using var context = new BunitContext();
        var pair = context.Render<PairedPrizeHost>(parameters =>
            parameters
                .Add(host => host.InitialSmallest, "80")
                .Add(host => host.InitialLargest, "100")
        );

        Step(pair, "smallest-prize", "increment");
        Value(pair, "smallest-prize").ShouldBe("90");

        Step(pair, "smallest-prize", "increment");
        Step(pair, "smallest-prize", "increment");
        Value(pair, "smallest-prize").ShouldBe("100");
        Value(pair, "largest-prize").ShouldBe("100");

        Step(pair, "largest-prize", "increment");
        Step(pair, "smallest-prize", "increment");
        Value(pair, "largest-prize").ShouldBe("110");
        Value(pair, "smallest-prize").ShouldBe("110");

        Step(pair, "largest-prize", "decrement");
        Value(pair, "largest-prize").ShouldBe("110");

        Step(pair, "smallest-prize", "decrement");
        Step(pair, "largest-prize", "decrement");
        Value(pair, "smallest-prize").ShouldBe("100");
        Value(pair, "largest-prize").ShouldBe("100");
    }

    private static string Press(string start, string action)
    {
        using var context = new BunitContext();
        var value = start;
        var stepper = Render(context, start, next => value = next);
        stepper.Find($"[data-action='{action}']").Click();
        return value;
    }

    private static string PressReward(string start, string action)
    {
        using var context = new BunitContext();
        var value = start;
        var stepper = context.Render<StudioStepper>(parameters =>
            parameters
                .Add(component => component.Id, "guess-profile-1-winning-reward")
                .Add(component => component.AriaLabel, "Points for a winning guess")
                .Add(component => component.Unit, "points")
                .Add(component => component.Step, new BigInteger(10))
                .Add(component => component.Minimum, BigInteger.Zero)
                .Add(component => component.Maximum, _pointCeiling)
                .Add(component => component.Value, start)
                .Add(component => component.ValueChanged, next => value = next)
        );
        stepper.Find($"[data-action='{action}']").Click();
        return value;
    }

    private static IRenderedComponent<StudioStepper> Render(
        BunitContext context,
        string value,
        Action<string> write
    ) =>
        context.Render<StudioStepper>(parameters =>
            parameters
                .Add(component => component.Id, "guessing-pin-duration")
                .Add(component => component.AriaLabel, "Pin duration in seconds")
                .Add(component => component.Unit, "seconds")
                .Add(component => component.Step, new BigInteger(30))
                .Add(component => component.Minimum, new BigInteger(30))
                .Add(component => component.Maximum, new BigInteger(1800))
                .Add(component => component.Value, value)
                .Add(component => component.ValueChanged, write)
        );

    private static void Step(IRenderedComponent<PairedPrizeHost> pair, string id, string action) =>
        Stepper(pair, id).Find($"[data-action='{action}']").Click();

    private static string? Value(IRenderedComponent<PairedPrizeHost> pair, string id) =>
        Stepper(pair, id).Find("input").GetAttribute("value");

    private static IRenderedComponent<StudioStepper> Stepper(
        IRenderedComponent<PairedPrizeHost> pair,
        string id
    ) => pair.FindComponents<StudioStepper>().Single(stepper => stepper.Instance.Id == id);

    /// <summary>
    /// Mirrors the production parent contract Points will use: the page owns both prizes, hands
    /// each stepper the sibling's current value as its own bound, and re-renders both whenever
    /// either changes.
    /// </summary>
    private sealed class PairedPrizeHost : ComponentBase
    {
        private string _smallest = string.Empty;
        private string _largest = string.Empty;

        [Parameter]
        public string InitialSmallest { get; set; } = string.Empty;

        [Parameter]
        public string InitialLargest { get; set; } = string.Empty;

        protected override void OnInitialized()
        {
            _smallest = InitialSmallest;
            _largest = InitialLargest;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<StudioStepper>(0);
            builder.AddComponentParameter(1, nameof(StudioStepper.Id), "smallest-prize");
            builder.AddComponentParameter(2, nameof(StudioStepper.AriaLabel), "Smallest prize");
            builder.AddComponentParameter(3, nameof(StudioStepper.Step), new BigInteger(10));
            builder.AddComponentParameter(4, nameof(StudioStepper.Minimum), BigInteger.Zero);
            builder.AddComponentParameter(5, nameof(StudioStepper.Maximum), Bound(_largest));
            builder.AddComponentParameter(6, nameof(StudioStepper.Value), _smallest);
            builder.AddComponentParameter(
                7,
                nameof(StudioStepper.ValueChanged),
                EventCallback.Factory.Create<string>(this, next => _smallest = next)
            );
            builder.CloseComponent();

            builder.OpenComponent<StudioStepper>(10);
            builder.AddComponentParameter(11, nameof(StudioStepper.Id), "largest-prize");
            builder.AddComponentParameter(12, nameof(StudioStepper.AriaLabel), "Largest prize");
            builder.AddComponentParameter(13, nameof(StudioStepper.Step), new BigInteger(10));
            builder.AddComponentParameter(14, nameof(StudioStepper.Minimum), Bound(_smallest));
            builder.AddComponentParameter(15, nameof(StudioStepper.Maximum), _pointCeiling);
            builder.AddComponentParameter(16, nameof(StudioStepper.Value), _largest);
            builder.AddComponentParameter(
                17,
                nameof(StudioStepper.ValueChanged),
                EventCallback.Factory.Create<string>(this, next => _largest = next)
            );
            builder.CloseComponent();
        }

        private static BigInteger? Bound(string value) =>
            BigInteger.TryParse(
                value.Trim(),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed
            )
                ? parsed
                : null;
    }
}
