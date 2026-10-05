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
    public async Task RefusalKeepsBrowserStateAndRetryClearsFeedbackWithoutReportingAnInteropFault()
    {
        using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/EditorWorkspace.razor.js");
        var browser = module.SetupModule("createWorkspace", _ => true);
        var operation = browser.Setup<EditorWorkspace.FullscreenResult>("toggleFullscreen");
        _ = operation.SetResult(EditorWorkspace.FullscreenResult.EnterRefused);
        var faults = 0;
        var workspace = context.Render<EditorWorkspace>(parameters =>
            parameters.Add(component => component.InteropFailure, _ => faults++)
        );
        workspace.Find("button[aria-label='Focus']").Click();
        workspace.Find("button[aria-label='Full screen']").Click();
        workspace.WaitForAssertion(() =>
        {
            workspace.Find("[role=alert]").TextContent.ShouldNotBeNullOrWhiteSpace();
            workspace
                .Find("button[aria-label='Full screen']")
                .GetAttribute("aria-pressed")
                .ShouldBe("false");
        });
        _ = operation.SetResult(EditorWorkspace.FullscreenResult.Completed);
        workspace.Find("button[aria-label='Full screen']").Click();
        workspace.WaitForAssertion(() => workspace.FindAll("[role=alert]").ShouldBeEmpty());
        await workspace.Instance.BrowserFullscreenChangedAsync(true);
        _ = operation.SetResult(EditorWorkspace.FullscreenResult.ExitRefused);
        workspace.Find("button[aria-label='Exit Full screen']").Click();
        workspace.WaitForAssertion(() =>
        {
            workspace.Find("[role=alert]").TextContent.ShouldNotBeNullOrWhiteSpace();
            workspace
                .Find("button[aria-label='Exit Full screen']")
                .GetAttribute("aria-pressed")
                .ShouldBe("true");
        });
        _ = operation.SetResult(EditorWorkspace.FullscreenResult.Completed);
        workspace.Find("button[aria-label='Exit Full screen']").Click();
        await workspace.Instance.BrowserFullscreenChangedAsync(false);
        workspace.WaitForAssertion(() => workspace.FindAll("[role=alert]").ShouldBeEmpty());
        workspace.Instance.FocusMode.ShouldBeTrue();
        faults.ShouldBe(0);
        _ = browser.SetupVoid("dispose", _ => true).SetVoidResult();
    }

    [Test]
    public void ExceptionalInteropFailureIsReportedWithoutInventingFullscreenState()
    {
        using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/EditorWorkspace.razor.js");
        var browser = module.SetupModule("createWorkspace", _ => true);
        _ = browser
            .Setup<EditorWorkspace.FullscreenResult>("toggleFullscreen")
            .SetException(new JSException("Module failed"));
        var faults = 0;
        var workspace = context.Render<EditorWorkspace>(parameters =>
            parameters.Add(component => component.InteropFailure, _ => faults++)
        );
        workspace.Find("button[aria-label='Full screen']").Click();
        workspace.WaitForAssertion(() =>
        {
            faults.ShouldBe(1);
            workspace.Find("[role=alert]").TextContent.ShouldNotBeNullOrWhiteSpace();
            workspace
                .Find("button[aria-label='Full screen']")
                .GetAttribute("aria-pressed")
                .ShouldBe("false");
        });
        _ = browser.SetupVoid("dispose", _ => true).SetVoidResult();
    }
}
