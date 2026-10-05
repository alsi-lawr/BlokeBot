using BlokeBot.Core.Components;
using Bunit;
using Microsoft.JSInterop;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class EditorWorkspaceTests
{
    [Test]
    public async Task ActualBrowserExitUpdatesControlsWithoutExitingApplicationFocus()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        bool? focus = null;
        var workspace = context.Render<EditorWorkspace>(parameters =>
            parameters.Add(component => component.FocusChanged, value => focus = value)
        );
        workspace.Find("button[aria-label='Focus']").Click();
        focus.ShouldBe(true);

        await workspace.Instance.BrowserFullscreenChangedAsync(true);
        workspace
            .Find("button[aria-label='Exit Full screen']")
            .GetAttribute("aria-pressed")
            .ShouldBe("true");
        await workspace.Instance.BrowserFullscreenChangedAsync(false);
        workspace
            .Find("button[aria-label='Full screen']")
            .GetAttribute("aria-pressed")
            .ShouldBe("false");
        workspace.Instance.FocusMode.ShouldBeTrue();
        workspace.Find("button[aria-label='Exit Focus']").Click();
        focus.ShouldBe(false);
    }

    [Test]
    public void DeniedBrowserOperationShowsFailureAndKeepsActualState()
    {
        using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/EditorWorkspace.razor.js");
        var browser = module.SetupModule("createWorkspace", _ => true);
        _ = browser
            .SetupVoid("toggleFullscreen")
            .SetException(new JSException("Denied by browser"));
        JSException? reported = null;
        var workspace = context.Render<EditorWorkspace>(parameters =>
            parameters.Add(component => component.BrowserFailure, failure => reported = failure)
        );

        workspace.Find("button[aria-label='Full screen']").Click();

        workspace.WaitForAssertion(() =>
        {
            _ = reported.ShouldNotBeNull();
            workspace.Find("[role=alert]").TextContent.ShouldNotBeNullOrWhiteSpace();
            workspace
                .Find("button[aria-label='Full screen']")
                .GetAttribute("aria-pressed")
                .ShouldBe("false");
        });
        _ = browser.SetupVoid("dispose", _ => true).SetVoidResult();
    }
}
