using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Page;
using BlokeBot.Persistence.Models;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationActivationStatusPanelTests
{
    [Test]
    public async Task PendingFailedAndManualFollowUpStates_ExposeTheOwnedRecoveryJourneys()
    {
        await using var context = new BunitContext();
        var retryCount = 0;
        var panel = context.Render<ConfigurationActivationStatusPanel>(parameters =>
            parameters
                .Add(
                    value => value.Activation,
                    View(ConfigurationActivationStatus.Pending, "automatic-work-canceled")
                )
                .Add(value => value.Retry, () => retryCount++)
        );
        panel.Find("[role='status']").TextContent.ShouldNotBeNullOrWhiteSpace();
        panel.FindAll("button").ShouldBeEmpty();

        panel.Render(parameters =>
            parameters
                .Add(
                    value => value.Activation,
                    View(ConfigurationActivationStatus.Failed, "automatic-work-failed")
                )
                .Add(value => value.Retry, () => retryCount++)
        );

        await panel.Find("button").ClickAsync(new());
        retryCount.ShouldBe(1);
        panel.FindAll("a[href='/alerts']").ShouldBeEmpty();

        panel.Render(parameters =>
            parameters
                .Add(
                    value => value.Activation,
                    View(ConfigurationActivationStatus.ManualFollowUp, "provider-required")
                )
                .Add(value => value.Retry, () => retryCount++)
        );
        await panel.Find("button").ClickAsync(new());

        retryCount.ShouldBe(2);
        panel.Find("a[href='/alerts']").GetAttribute("href").ShouldBe("/alerts");
    }

    private static ConfigurationActivationView View(
        ConfigurationActivationStatus status,
        string issueCode
    ) =>
        new(
            Guid.NewGuid(),
            status,
            1,
            [new(issueCode, "The durable configuration is safe; complete this recovery step.")]
        );
}
