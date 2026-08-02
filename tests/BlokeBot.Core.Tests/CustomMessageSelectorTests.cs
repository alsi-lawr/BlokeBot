using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomMessageSelectorTests
{
    [Test]
    public void SequentialSnapshot_SelectingMessage_ReturnsTextAndNextIndexWithoutMutation()
    {
        string[] variants = ["first", "second"];
        var snapshot = new CustomMessageSelectionSnapshot(
            CustomMessageSelectionMode.Sequential,
            1,
            variants
        );
        variants[1] = "mutated";

        var selection = new CustomMessageSelector()
            .Select(snapshot)
            .Match(
                static selected => $"{selected.Text}:{selected.NextVariantIndex}",
                static () => "none"
            );

        selection.ShouldBe("second:0");
        snapshot.CurrentVariantIndex.ShouldBe(1);
        snapshot.Variants.ShouldBe(["first", "second"]);
    }

    [Test]
    public void EmptySnapshot_SelectingMessage_ReturnsNoSelection()
    {
        var snapshot = new CustomMessageSelectionSnapshot(CustomMessageSelectionMode.First, 0, []);

        var selection = new CustomMessageSelector()
            .Select(snapshot)
            .Match(static _ => "selected", static () => "none");

        selection.ShouldBe("none");
    }
}
