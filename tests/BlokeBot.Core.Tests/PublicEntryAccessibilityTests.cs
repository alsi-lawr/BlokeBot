using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Core.Features.Toasts;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PublicEntryAccessibilityTests
{
    private const string _channelHint =
        "You can enter samplechannel, @samplechannel, or #samplechannel.";
    private const string _channelRequired = "Enter a Twitch channel name.";
    private const string _whisperRecovery =
        "Private replies need a connected custom bot. Open Channel setup to connect one.";

    [Test]
    public void PublicLeaderboardPrompt_EmptyChannel_ShowsAccessibleValidation()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var prompt = context.Render<PublicLeaderboardPrompt>();
        var label = prompt.Find("label[for='public-leaderboard-channel']");
        var input = prompt.Find("#public-leaderboard-channel");
        var error = prompt.Find("#public-leaderboard-channel-error");

        label.TextContent.Trim().ShouldBe("Twitch channel name");
        prompt.Find("#public-leaderboard-channel-hint").TextContent.Trim().ShouldBe(_channelHint);
        input
            .GetAttribute("aria-describedby")
            .ShouldBe("public-leaderboard-channel-hint public-leaderboard-channel-error");
        input.GetAttribute("aria-invalid").ShouldBe("false");
        error.HasAttribute("hidden").ShouldBeTrue();

        prompt.Find("button").Click();

        prompt.Find("#public-leaderboard-channel").GetAttribute("aria-invalid").ShouldBe("true");
        error = prompt.Find("#public-leaderboard-channel-error");
        error.HasAttribute("hidden").ShouldBeFalse();
        error.TextContent.Trim().ShouldBe(_channelRequired);
    }

    [Test]
    [Arguments("samplechannel")]
    [Arguments("@SampleChannel")]
    [Arguments("#SampleChannel")]
    public void PublicLeaderboardPrompt_PrefixedChannel_NavigatesWithLoginNormalization(
        string channel
    )
    {
        using var context = new BunitContext();
        var prompt = context.Render<PublicLeaderboardPrompt>();

        prompt.Find("#public-leaderboard-channel").Input(channel);
        prompt.Find("button").Click();

        context
            .Services.GetRequiredService<NavigationManager>()
            .Uri.ShouldEndWith("/guessing/leaderboard/samplechannel");
    }

    [Test]
    public void DisabledWhisperControls_RenderRecoveryAndStableChannelSetupLink()
    {
        using var context = new BunitContext();
        var delivery = context.Render<ReplyDeliverySettingsSection>(parameters =>
            parameters
                .Add(component => component.Delivery, new ReplyDeliveryEditor())
                .Add(
                    component => component.Options,
                    [new ReplyDeliveryOption("Balance", "balance")]
                )
                .Add(component => component.WhisperResponsesEnabled, false)
        );
        delivery.Find("button.disclosure-trigger").Click();

        AssertWhisperRecovery(delivery, "reply-delivery-whisper-recovery");

        var guesses = context.Render<GuessOptionsSettingsSection>(parameters =>
            parameters
                .Add(component => component.Options, new List<GuessOptionEditor>())
                .Add(component => component.WhisperResponsesEnabled, false)
        );

        AssertWhisperRecovery(guesses, "answer-replies-whisper-recovery");
    }

    [Test]
    public void TextAreaField_Rendering_AssociatesEachLabelWithItsTextArea()
    {
        using var context = new BunitContext();
        var first = context.Render<TextAreaField>(parameters =>
            parameters.Add(component => component.Label, "First reply")
        );
        var second = context.Render<TextAreaField>(parameters =>
            parameters.Add(component => component.Label, "Second reply")
        );

        var firstTextArea = first.Find("textarea");
        var secondTextArea = second.Find("textarea");

        first.Find("label").GetAttribute("for").ShouldBe(firstTextArea.Id);
        second.Find("label").GetAttribute("for").ShouldBe(secondTextArea.Id);
        firstTextArea.Id.ShouldNotBe(secondTextArea.Id);
    }

    [Test]
    public void MobileDrawer_OpenAndClose_UpdatesInertStateAndRestoresFocus()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<NavMenu>();
        context.ComponentFactories.AddStub<TopBarControls>();
        context.ComponentFactories.AddStub<ThemeToggle>();
        context.ComponentFactories.AddStub<PageHelpButton>();
        context.ComponentFactories.AddStub<ToastHost>();
        var layout = context.Render<MainLayout>(parameters =>
            parameters.Add(
                component => component.Body,
                builder => builder.AddContent(0, "Page body")
            )
        );
        var drawer = layout.Find("aside[aria-label='Main navigation']");
        var menuButton = layout.Find("button[aria-label='Open navigation menu']");

        drawer.HasAttribute("inert").ShouldBeTrue();
        drawer.GetAttribute("aria-hidden").ShouldBe("true");
        menuButton.GetAttribute("aria-expanded").ShouldBe("false");

        menuButton.Click();

        drawer = layout.Find("aside[aria-label='Main navigation']");
        menuButton = layout.Find("button[aria-label='Open navigation menu']");
        drawer.HasAttribute("inert").ShouldBeFalse();
        drawer.GetAttribute("aria-hidden").ShouldBe("false");
        menuButton.GetAttribute("aria-expanded").ShouldBe("true");
        var openingFocus = context.JSInterop.VerifyInvoke("blokeBotNavigation.focus");
        openingFocus.Arguments[0].ShouldBe("mobile-navigation-drawer");

        layout.Find("button[aria-label='Close navigation menu']").Click();

        drawer = layout.Find("aside[aria-label='Main navigation']");
        menuButton = layout.Find("button[aria-label='Open navigation menu']");
        drawer.HasAttribute("inert").ShouldBeTrue();
        drawer.GetAttribute("aria-hidden").ShouldBe("true");
        menuButton.GetAttribute("aria-expanded").ShouldBe("false");
        context.JSInterop.VerifyInvoke("blokeBotNavigation.focus", 2);
        var focusInvocations = context
            .JSInterop.Invocations.Where(invocation =>
                invocation.Identifier == openingFocus.Identifier
            )
            .ToArray();
        focusInvocations[^1].Arguments[0].ShouldBe("mobile-navigation-menu-button");
    }

    private static void AssertWhisperRecovery<TComponent>(
        IRenderedComponent<TComponent> component,
        string recoveryId
    )
        where TComponent : IComponent
    {
        var checkbox = component.Find("input[type='checkbox']");
        var recovery = component.Find($"#{recoveryId}");
        var link = recovery.QuerySelector("a");

        checkbox.HasAttribute("disabled").ShouldBeTrue();
        checkbox.GetAttribute("aria-describedby").ShouldBe(recoveryId);
        recovery.TextContent.Trim().ShouldBe(_whisperRecovery);
        link.ShouldNotBeNull();
        link.GetAttribute("href").ShouldBe("/host#custom-bot");
    }
}
