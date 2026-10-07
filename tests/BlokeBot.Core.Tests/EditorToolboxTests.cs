using BlokeBot.Core.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class EditorToolboxTests
{
    [Test]
    public void Search_ReportsNativeInputAndEmptyResults_EscapeReturnsClosureToOwner()
    {
        using var context = new BunitContext();
        string? searched = null;
        var closed = 0;
        var toolbox = context.Render<EditorToolbox>(parameters =>
            parameters
                .Add(component => component.Id, "test-toolbox")
                .Add(component => component.Purpose, "Add an item")
                .Add(component => component.ResultsTitle, "Items")
                .Add(component => component.EmptyMessage, "No items match.")
                .Add(component => component.SearchChanged, value => searched = value)
                .Add(component => component.Close, () => closed++)
        );

        toolbox.Find("input").Input("unknown");
        searched.ShouldBe("unknown");
        toolbox.Find("[role=status]").TextContent.ShouldBe("No items match.");
        toolbox.Find("input").KeyDown(new KeyboardEventArgs { Key = "Delete" });
        closed.ShouldBe(0);
        toolbox.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        closed.ShouldBe(1);
    }

    [Test]
    public void Row_AvailabilityPreventsActivation_WithoutHidingItsReason()
    {
        using var context = new BunitContext();
        var activated = 0;
        var row = context.Render<EditorToolboxRow>(parameters =>
            parameters
                .Add(component => component.Name, "Current item")
                .Add(component => component.Purpose, "Adds an item.")
                .Add(component => component.AccessibleLabel, "Current item. Needs setup.")
                .Add(component => component.Status, "Needs setup.")
                .Add(component => component.Available, false)
                .Add(component => component.Activate, () => activated++)
        );

        row.Find("button").Click();
        activated.ShouldBe(0);
        row.Find("button").GetAttribute("aria-disabled").ShouldBe("true");
        row.Find(".editor-toolbox-availability").TextContent.ShouldBe("Needs setup.");

        row.Render(parameters => parameters.Add(component => component.Available, true));
        row.Find("button").Click();
        activated.ShouldBe(1);
    }

    [Test]
    public void Toggle_HasIconOnlyAccessibleExpandedAction_AndDelegatesToOwner()
    {
        using var context = new BunitContext();
        var toggled = 0;
        var toggle = context.Render<EditorToolboxToggle>(parameters =>
            parameters
                .Add(component => component.Open, false)
                .Add(component => component.Toggle, () => toggled++)
        );
        var button = toggle.Find("button");
        button.TextContent.Trim().ShouldBeEmpty();
        button.GetAttribute("aria-label").ShouldBe("Toolbox");
        button.GetAttribute("aria-expanded").ShouldBe("false");
        button.Click();
        toggled.ShouldBe(1);
        toggle.Render(parameters => parameters.Add(component => component.Open, true));
        toggle.Find("button").GetAttribute("aria-expanded").ShouldBe("true");
    }
}
