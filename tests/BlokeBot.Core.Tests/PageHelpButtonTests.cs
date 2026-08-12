using BlokeBot.Core.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PageHelpButtonTests
{
    private const string _focusInterop = "Blazor._internal.domWrapper.focus";

    [Test]
    [Arguments("https://guide.example.com", "https://guide.example.com/community/bounties")]
    [Arguments("https://guide.example.com/", "https://guide.example.com/community/bounties")]
    [Arguments("http://guide.example.com", "http://guide.example.com/community/bounties")]
    [Arguments("https://example.com/docs", "https://example.com/docs/community/bounties")]
    [Arguments("https://example.com/docs/", "https://example.com/docs/community/bounties")]
    [Arguments("  https://example.com/docs  ", "https://example.com/docs/community/bounties")]
    public void AcceptedBase_KeepsItsPathPrefixWhenResolvingAMappedGuide(
        string configured,
        string expected
    ) => HelpSiteGuide.Resolve(configured, "/community/bounties")?.ToString().ShouldBe(expected);

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("/guide")]
    [Arguments("guide.example.com")]
    [Arguments("://guide.example.com")]
    [Arguments("ftp://guide.example.com/")]
    [Arguments("javascript:alert('x')")]
    [Arguments("file:///etc/guide")]
    [Arguments("https://user:secret@guide.example.com/")]
    [Arguments("https://guide.example.com/?tenant=1")]
    [Arguments("https://guide.example.com/#top")]
    public void RejectedBase_ResolvesToNoLinkAtAll(string? configured)
    {
        HelpSiteGuide.BaseAddress(configured).ShouldBeNull();
        HelpSiteGuide.Resolve(configured, "/community/bounties").ShouldBeNull();
    }

    [Test]
    public void RejectedBase_IsNotAStartupValidationFailure() =>
        BlokeBotOptionsValidation
            .IsValid(new BlokeBotOptions { HelpSiteBaseUrl = "not-a-url" })
            .ShouldBeTrue();

    [Test]
    public void HelpTrigger_IsAKeyboardActivatedButtonThatOwnsItsLabelledPopover()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");
        var trigger = help.Find("button[aria-label='Page help']");

        trigger.GetAttribute("type").ShouldBe("button");
        trigger.GetAttribute("aria-expanded").ShouldBe("false");
        trigger.GetAttribute("aria-controls").ShouldBe("page-help-popover");
        help.FindAll("#page-help-popover").ShouldBeEmpty();

        trigger.Click();

        help.Find("button[aria-label='Page help']").GetAttribute("aria-expanded").ShouldBe("true");
        var popover = help.Find("#page-help-popover");
        popover.GetAttribute("aria-labelledby").ShouldBe("page-help-title");
        _ = help.Find("#page-help-title");
    }

    [Test]
    public void HelpTrigger_TogglesClosedAndKeepsFocusOnTheTrigger()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");

        help.Find("button[aria-label='Page help']").Click();
        help.Find("button[aria-label='Page help']").Click();

        help.FindAll("#page-help-popover").ShouldBeEmpty();
        help.Find("button[aria-label='Page help']").GetAttribute("aria-expanded").ShouldBe("false");
    }

    [Test]
    public void CloseButton_ClosesThePopoverAndRestoresFocusToTheTrigger()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");
        help.Find("button[aria-label='Page help']").Click();
        var focusCalls = FocusCalls(context);

        help.Find("button[aria-label='Close help']").Click();

        help.FindAll("#page-help-popover").ShouldBeEmpty();
        FocusCalls(context).ShouldBe(focusCalls + 1);
    }

    [Test]
    public void Escape_ClosesThePopoverAndRestoresFocusToTheTrigger()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");
        help.Find("button[aria-label='Page help']").Click();
        var focusCalls = FocusCalls(context);

        help.Find("#page-help-popover").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        help.FindAll("#page-help-popover").ShouldBeEmpty();
        FocusCalls(context).ShouldBe(focusCalls + 1);
    }

    [Test]
    public void Navigation_DismissesThePopoverWithoutStealingFocusBackToTheTrigger()
    {
        using var context = CreateContext(null);
        var help = RenderAt(context, "/queues");
        help.Find("button[aria-label='Page help']").Click();
        var focusCalls = FocusCalls(context);

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/bounties");

        help.WaitForAssertion(() => help.FindAll("#page-help-popover").ShouldBeEmpty());
        FocusCalls(context).ShouldBe(focusCalls);
    }

    private static IRenderedComponent<PageHelpButton> RenderAt(
        BunitContext context,
        string location
    )
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(location);
        return context.Render<PageHelpButton>();
    }

    private static BunitContext CreateContext(string? helpSiteBaseUrl)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = context.Services.AddScoped<DashboardFragmentState>();
        _ = context.Services.AddSingleton<IOptions<BlokeBotOptions>>(
            Options.Create(new BlokeBotOptions { HelpSiteBaseUrl = helpSiteBaseUrl })
        );
        return context;
    }

    private static int FocusCalls(BunitContext context) =>
        context.JSInterop.Invocations.Count(invocation => invocation.Identifier == _focusInterop);
}
