using BlokeBot.Core.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class EditorToolboxTests
{
    [Test]
    public void Search_ReportsNativeInput_EscapeReturnsClosureToOwner()
    {
        using var context = new BunitContext();
        string? searched = null;
        var closed = 0;
        var toolbox = context.Render<EditorToolbox>(parameters =>
            parameters
                .Add(component => component.Id, "test-toolbox")
                .Add(component => component.Purpose, "Add an item")
                .Add(component => component.ResultsTitle, "Items")
                .Add(component => component.SearchChanged, value => searched = value)
                .Add(component => component.Close, () => closed++)
        );

        toolbox.Find("input").Input("unknown");
        searched.ShouldBe("unknown");
        toolbox.Find("input").KeyDown(new KeyboardEventArgs { Key = "Delete" });
        closed.ShouldBe(0);
        toolbox.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        closed.ShouldBe(1);
    }
}
