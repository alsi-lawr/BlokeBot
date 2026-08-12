using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostedChannelRemovalUiTests
{
    [Test]
    public void EmptyOrMismatchedConfirmation_Removing_DoesNotInvokeDeletionOrAnimation()
    {
        using var context = new BunitContext();
        var removalCount = 0;
        var row = context.Render<HostedChannelRow>(parameters =>
            parameters
                .Add(component => component.Host, Host())
                .Add(
                    component => component.RemoveHost,
                    _ =>
                    {
                        removalCount++;
                        return Task.CompletedTask;
                    }
                )
        );

        row.FindAll("button").Single(button => button.TextContent.Trim() == "Remove").Click();

        _ = row.Find("[data-channel-removal-dialog]");
        var confirm = row.Find("button[aria-label='Permanently remove channel']");
        confirm.HasAttribute("disabled").ShouldBeTrue();
        confirm.Click();
        removalCount.ShouldBe(0);

        row.Find("input[aria-label='Type channel login to confirm removal']").Input("other");
        confirm = row.Find("button[aria-label='Permanently remove channel']");
        confirm.HasAttribute("disabled").ShouldBeTrue();
        confirm.Click();
        removalCount.ShouldBe(0);

        row.FindAll("[data-channel-removal-dialog] button")
            .Single(button => button.TextContent.Trim() == "Cancel")
            .Click();
        row.FindAll("[data-channel-removal-dialog]").ShouldBeEmpty();
        removalCount.ShouldBe(0);
    }

    [Test]
    public async Task NormalizedMatchingConfirmation_Removing_InvokesExistingCallbackOnce()
    {
        using var context = new BunitContext();
        var removedHostIds = new List<int>();
        var row = context.Render<HostedChannelRow>(parameters =>
            parameters
                .Add(component => component.Host, Host())
                .Add(
                    component => component.RemoveHost,
                    hostId =>
                    {
                        removedHostIds.Add(hostId);
                        return Task.CompletedTask;
                    }
                )
        );
        row.FindAll("button").Single(button => button.TextContent.Trim() == "Remove").Click();

        row.Find("input[aria-label='Type channel login to confirm removal']")
            .Input("  @StReAmEr  ");
        var confirm = row.Find("button[aria-label='Permanently remove channel']");
        confirm.HasAttribute("disabled").ShouldBeFalse();

        await confirm.ClickAsync(new());

        row.WaitForAssertion(() =>
        {
            removedHostIds.ShouldBe([42]);
            row.FindAll("[data-channel-removal-dialog]").ShouldBeEmpty();
        });
    }

    private static HostedChannelAdminView Host() =>
        new(
            42,
            "streamer",
            "Streamer",
            null,
            true,
            new HostedChannelRuntimeLifecycle.Stopped(null)
        );
}
