using BlokeBot.Core.Components;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Core.Features.Replies;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PublicEntryAccessibilityTests
{
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

        prompt.Find("input").Input(channel);
        prompt.Find("button").Click();

        context
            .Services.GetRequiredService<NavigationManager>()
            .Uri.ShouldEndWith("/guessing/leaderboard/samplechannel");
    }

    [Test]
    public void DisabledWhisperControls_OfferChannelSetupRecovery()
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
        delivery.Find("input[type='checkbox']").HasAttribute("disabled").ShouldBeTrue();
        _ = delivery.Find("a[href='/host#custom-bot']").ShouldNotBeNull();
    }
}
