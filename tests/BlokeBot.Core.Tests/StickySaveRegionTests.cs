using BlokeBot.Core.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class StickySaveRegionTests
{
    [Test]
    public void DashboardSlots_KeepOrdinaryActionsOutsideTheSaveRegion()
    {
        using var context = CreateContext();
        RenderFragment ordinaryAction = builder =>
            builder.AddMarkupContent(0, "<button data-ordinary-action></button>");
        RenderFragment saveAction = builder =>
            builder.AddMarkupContent(0, "<button data-save-action></button>");
        RenderFragment content = builder => builder.AddMarkupContent(0, "<div></div>");

        var page = context.Render<DashboardPage>(parameters =>
            parameters
                .Add(component => component.Title, "Settings")
                .Add(component => component.Actions, ordinaryAction)
                .Add(component => component.SaveAction, saveAction)
                .Add(component => component.ChildContent, content)
        );

        page.FindAll(".page-header__actions [data-ordinary-action]").Count.ShouldBe(1);
        page.FindAll("[data-save-active='true'] [data-save-action]").Count.ShouldBe(1);
        page.FindAll("[data-save-active] [data-ordinary-action]").ShouldBeEmpty();
    }

    [Test]
    [Arguments(PageSaveFeedbackKind.Dirty, "status")]
    [Arguments(PageSaveFeedbackKind.Saving, "status")]
    [Arguments(PageSaveFeedbackKind.Success, "status")]
    [Arguments(PageSaveFeedbackKind.Validation, "alert")]
    [Arguments(PageSaveFeedbackKind.Failure, "alert")]
    public void Feedback_UsesTheSemanticRoleForItsKind(
        PageSaveFeedbackKind kind,
        string expectedRole
    )
    {
        using var context = CreateContext();
        RenderFragment action = builder => builder.AddMarkupContent(0, "<button></button>");

        var region = context.Render<StickySaveRegion>(parameters =>
            parameters
                .Add(component => component.Feedback, new PageSaveFeedback("Feedback", kind))
                .Add(component => component.ChildContent, action)
        );

        region.Find("[data-save-feedback]").GetAttribute("role").ShouldBe(expectedRole);
    }

    [Test]
    public void EnrollmentChanges_PreserveOneDisabledAction()
    {
        using var context = CreateContext();
        var host = context.Render<StickySaveRegionHost>(parameters =>
            parameters
                .Add(component => component.ActiveEditor, 1)
                .Add(component => component.Disabled, true)
        );

        host.FindAll("[data-save-active='true']").Count.ShouldBe(1);
        host.FindAll("button").Count.ShouldBe(2);
        host.FindAll("button:disabled").Count.ShouldBe(2);

        host.Render(parameters =>
            parameters
                .Add(component => component.ActiveEditor, 2)
                .Add(component => component.Disabled, true)
        );

        host.FindAll("[data-save-active='true']").Count.ShouldBe(1);
        host.Find("[data-save-active='true'] button").GetAttribute("data-editor").ShouldBe("2");
        host.FindAll("button").Count.ShouldBe(2);
        host.FindAll("button:disabled").Count.ShouldBe(2);
    }

    [Test]
    public void InactiveIntent_RemainsInFlowWithoutStickyEnrollment()
    {
        using var context = CreateContext();
        RenderFragment action = builder =>
            builder.AddMarkupContent(0, "<button data-dynamic-action></button>");

        var region = context.Render<StickySaveRegion>(parameters =>
            parameters
                .Add(component => component.Active, false)
                .Add(component => component.ChildContent, action)
        );

        region.FindAll("button").Count.ShouldBe(1);
        region.Find(".sticky-save-region").GetAttribute("data-save-active").ShouldBe("false");
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
