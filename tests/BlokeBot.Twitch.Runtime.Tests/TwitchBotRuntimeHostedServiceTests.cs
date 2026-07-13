using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchBotRuntimeHostedServiceTests
{
    [Test]
    public async Task IrcConfigured_RunningSelectedStrategy_RunsOnlyIrcStrategy()
    {
        var irc = new RecordingRuntimeStrategy(TwitchBotRuntime.Irc);
        var eventSub = new RecordingRuntimeStrategy(TwitchBotRuntime.EventSub);
        using var service = CreateService(TwitchBotRuntime.Irc, irc, eventSub);

        await service.RunSelectedStrategyAsync(CancellationToken.None);

        irc.RunCount.ShouldBe(1);
        eventSub.RunCount.ShouldBe(0);
    }

    [Test]
    public async Task EventSubConfigured_RunningSelectedStrategy_RunsOnlyEventSubStrategy()
    {
        var irc = new RecordingRuntimeStrategy(TwitchBotRuntime.Irc);
        var eventSub = new RecordingRuntimeStrategy(TwitchBotRuntime.EventSub);
        using var service = CreateService(TwitchBotRuntime.EventSub, irc, eventSub);

        await service.RunSelectedStrategyAsync(CancellationToken.None);

        irc.RunCount.ShouldBe(0);
        eventSub.RunCount.ShouldBe(1);
    }

    [Test]
    public void SelectedRuntimeWithoutStrategy_ConstructingHostedService_RejectsMissingStrategy()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            CreateService(
                TwitchBotRuntime.Irc,
                new RecordingRuntimeStrategy(TwitchBotRuntime.EventSub)
            )
        );

        exception.Message.ShouldContain(nameof(TwitchBotRuntime.Irc));
        exception.Message.ShouldContain("No runtime strategy");
    }

    [Test]
    public void SelectedRuntimeWithDuplicateStrategies_ConstructingHostedService_RejectsConflict()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            CreateService(
                TwitchBotRuntime.Irc,
                new RecordingRuntimeStrategy(TwitchBotRuntime.Irc),
                new RecordingRuntimeStrategy(TwitchBotRuntime.Irc)
            )
        );

        exception.Message.ShouldContain(nameof(TwitchBotRuntime.Irc));
        exception.Message.ShouldContain("Multiple runtime strategies");
    }

    private static TwitchBotRuntimeHostedService CreateService(
        TwitchBotRuntime runtime,
        params ITwitchBotRuntimeStrategy[] strategies
    )
    {
        return new(
            TwitchBotSettings.FromOptions(new TwitchBotOptions { Runtime = runtime }),
            strategies
        );
    }

    private sealed class RecordingRuntimeStrategy(TwitchBotRuntime runtime)
        : ITwitchBotRuntimeStrategy
    {
        public TwitchBotRuntime Runtime { get; } = runtime;

        public int RunCount { get; private set; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            RunCount++;
            return Task.CompletedTask;
        }
    }
}
