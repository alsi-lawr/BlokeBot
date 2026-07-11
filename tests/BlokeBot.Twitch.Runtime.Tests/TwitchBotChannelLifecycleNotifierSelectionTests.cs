using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchBotChannelLifecycleNotifierSelectionTests
{
    [Test]
    public void NoOpNotifier_Selected_ProducesNoOpPolicy()
    {
        var policy = new TwitchBotChannelLifecycleNotifierSelection()
            .UseNoOpNotifier()
            .RequireSingle();

        policy.Kind.ShouldBe(TwitchBotChannelLifecycleNotifierKind.NoOp);
        policy.NotifierType.ShouldBe(typeof(NoOpTwitchBotChannelLifecycleNotifier));
    }

    [Test]
    public void HostedNotifier_Selected_ProducesHostedPolicy()
    {
        var policy = new TwitchBotChannelLifecycleNotifierSelection()
            .UseHostedNotifier<HostedNotifier>()
            .RequireSingle();

        policy.Kind.ShouldBe(TwitchBotChannelLifecycleNotifierKind.Hosted);
        policy.NotifierType.ShouldBe(typeof(HostedNotifier));
    }

    [Test]
    public void NoNotifier_Selected_RejectsMissingPolicy()
    {
        var selection = new TwitchBotChannelLifecycleNotifierSelection();

        var exception = Should.Throw<InvalidOperationException>(() =>
            selection.RequireSingle()
        );

        exception.Message.ShouldContain("none was selected");
    }

    [Test]
    public void ConflictingNotifiers_SelectedInEitherOrder_RejectsPolicy()
    {
        var noOpThenHosted = new TwitchBotChannelLifecycleNotifierSelection()
            .UseNoOpNotifier()
            .UseHostedNotifier<HostedNotifier>();
        var hostedThenNoOp = new TwitchBotChannelLifecycleNotifierSelection()
            .UseHostedNotifier<HostedNotifier>()
            .UseNoOpNotifier();

        var firstException = Should.Throw<InvalidOperationException>(() =>
            noOpThenHosted.RequireSingle()
        );
        var secondException = Should.Throw<InvalidOperationException>(() =>
            hostedThenNoOp.RequireSingle()
        );

        firstException.Message.ShouldContain("2 were selected");
        secondException.Message.ShouldContain("2 were selected");
    }

    [Test]
    public async Task NoOpNotifier_ReceivingLifecycleNotifications_Completes()
    {
        var notifier = new NoOpTwitchBotChannelLifecycleNotifier();

        await notifier.ChannelStartedAsync("streamer", CancellationToken.None);
        await notifier.ChannelStoppedAsync("streamer", CancellationToken.None);
    }

    private sealed class HostedNotifier : ITwitchBotChannelLifecycleNotifier
    {
        public Task ChannelStartedAsync(
            string channel,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task ChannelStoppedAsync(
            string channel,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}
