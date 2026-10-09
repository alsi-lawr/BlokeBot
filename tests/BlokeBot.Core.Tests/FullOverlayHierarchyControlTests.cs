using System.Text.Json;
using BlokeBot.Core.Features.Overlays.Full;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class FullOverlayHierarchyControlTests
{
    [Test]
    public void RelationAndDestinationUseTheSameExplicitCommandWhileInvalidTargetsStayDisabled()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var commands = new List<JsonElement>();
        var inspected = new List<JsonElement>();
        var view = View();
        var inspector = context.Render<FullOverlayInspector>(parameters =>
            parameters
                .Add(component => component.View, view)
                .Add(
                    component => component.Command,
                    command => commands.Add(JsonSerializer.SerializeToElement(command))
                )
                .Add(
                    component => component.HierarchyPreview,
                    command =>
                    {
                        var value = JsonSerializer.SerializeToElement(command);
                        inspected.Add(value);
                        return Task.FromResult(
                            new FullOverlayHierarchyFeedback(
                                value.GetProperty("relation").GetString() != "before",
                                "Current destination"
                            )
                        );
                    }
                )
        );
        inspector.Find("[data-fold=full-hierarchy]>button").Click();
        inspector.WaitForAssertion(() =>
            inspector.Find(".full-hierarchy-apply").HasAttribute("disabled").ShouldBeFalse()
        );
        inspector.Find("#full-hierarchy-destination").Change("element:b");
        inspector.Find(".full-hierarchy-relations button:nth-child(2)").Click();
        inspector.WaitForAssertion(() =>
            inspector.Find(".full-hierarchy-apply").HasAttribute("disabled").ShouldBeTrue()
        );
        commands.ShouldBeEmpty();
        inspector.Find(".full-hierarchy-relations button:nth-child(3)").Click();
        inspector.WaitForAssertion(() =>
            inspector.Find(".full-hierarchy-apply").HasAttribute("disabled").ShouldBeFalse()
        );
        inspector.Find(".full-hierarchy-apply").Click();
        commands.Single().GetProperty("relation").GetString().ShouldBe("after");
        commands.Single().GetProperty("target").GetString().ShouldBe("element:b");
        inspector.Find(".full-hierarchy-relations button:nth-child(4)").Click();
        inspector.Find("#full-hierarchy-destination").HasAttribute("disabled").ShouldBeTrue();
        inspected.Last().GetProperty("relation").GetString().ShouldBe("root");
    }

    private static FullOverlayEditorView View() =>
        new(
            7,
            false,
            "element:a",
            [
                new("element:a", null, "Heading", "h1", "#heading", null),
                new("element:b", null, "Panel", "section", "#panel", null),
            ],
            null,
            false,
            new Dictionary<string, string>(),
            "",
            "visual",
            []
        )
        {
            Members = ["element:a", "element:b"],
            SelectionVersion = 4,
            NativeStyles = new Dictionary<string, string>
            {
                ["position"] = "static",
                ["translate"] = "none",
            },
        };
}
