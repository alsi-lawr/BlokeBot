using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Readiness_InFlightLiveResultCannotPromoteReplacementConnection(bool chat)
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var flow = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        var x = HeldReadinessFor(f);
        using var observation = new ReadinessConnectionObservation(x.Runtime);
        var readiness = ReadinessFor(f, x.Runtime, x.Tokens);
        var held = x.Streams.HoldNext();
        var old = ObserveReadinessAsync(f, x.Runtime, chat, "old");
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        x.Connection.Current = new BotRuntimeStatus.Authorized();
        x.Connection.Current = new BotRuntimeStatus.Connected(["streamer"]);
        _ = held.Release.TrySetResult(
            new HostStreamLivenessOutcome.Live("old-stream", f.Clock.GetUtcNow())
        );
        await old;
        Configured(await ReadinessSourcesAsync(readiness, f.HostId), flow)
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
            .Reason.ShouldNotBeNullOrWhiteSpace();
        x.Streams.Current = new HostStreamLivenessOutcome.Live("old-stream", f.Clock.GetUtcNow());
        await ObserveReadinessAsync(f, x.Runtime, chat, "fresh");
        _ = Configured(await ReadinessSourcesAsync(readiness, f.HostId), flow)
            .State.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
    }

    [Test]
    [Arguments(false, ReadinessStaleResult.Offline)]
    [Arguments(false, ReadinessStaleResult.Unavailable)]
    [Arguments(false, ReadinessStaleResult.OlderStream)]
    [Arguments(true, ReadinessStaleResult.Offline)]
    [Arguments(true, ReadinessStaleResult.Unavailable)]
    [Arguments(true, ReadinessStaleResult.OlderStream)]
    public async Task Readiness_InFlightUnsuccessfulResultCannotEraseCurrentObservation(
        bool chat,
        ReadinessStaleResult result
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var flow = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        var x = HeldReadinessFor(f);
        using var observation = new ReadinessConnectionObservation(x.Runtime);
        var readiness = ReadinessFor(f, x.Runtime, x.Tokens);
        var held = x.Streams.HoldNext();
        var old = ObserveReadinessAsync(f, x.Runtime, chat, "old");
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        x.Connection.Current = new BotRuntimeStatus.Authorized();
        x.Connection.Current = new BotRuntimeStatus.Connected(["streamer"]);
        x.Streams.Current = new HostStreamLivenessOutcome.Live(
            "current-stream",
            f.Clock.GetUtcNow()
        );
        await ObserveReadinessAsync(f, x.Runtime, chat, "fresh");
        var current = Configured(await ReadinessSourcesAsync(readiness, f.HostId), flow).State;
        _ = current.ShouldBeOfType<TwitchEventSourceReadinessState.Ready>();
        _ = held.Release.TrySetResult(
            result switch
            {
                ReadinessStaleResult.Offline => new HostStreamLivenessOutcome.Offline(),
                ReadinessStaleResult.Unavailable => ReadinessProviderUnavailable(),
                ReadinessStaleResult.OlderStream => new HostStreamLivenessOutcome.Live(
                    "older-stream",
                    f.Clock.GetUtcNow().AddMinutes(-5)
                ),
            }
        );
        await old;
        Configured(await ReadinessSourcesAsync(readiness, f.HostId), flow).State.ShouldBe(current);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Readiness_InFlightLiveResultCannotReplaceCurrentUnavailableReason(bool chat)
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var flow = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        var x = HeldReadinessFor(f);
        using var observation = new ReadinessConnectionObservation(x.Runtime);
        var readiness = ReadinessFor(f, x.Runtime, x.Tokens);
        var held = x.Streams.HoldNext();
        var old = ObserveReadinessAsync(f, x.Runtime, chat, "old");
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        x.Connection.Current = new BotRuntimeStatus.Authorized();
        x.Connection.Current = new BotRuntimeStatus.Connected(["streamer"]);
        x.Streams.Current = ReadinessProviderUnavailable();
        await ObserveReadinessAsync(f, x.Runtime, chat, "fresh");
        var current = Configured(await ReadinessSourcesAsync(readiness, f.HostId), flow).State;
        current
            .ShouldBeOfType<TwitchEventSourceReadinessState.ObservationUnavailable>()
            .Reason.ShouldNotBeNullOrWhiteSpace();
        _ = held.Release.TrySetResult(
            new HostStreamLivenessOutcome.Live("old-stream", f.Clock.GetUtcNow())
        );
        await old;
        Configured(await ReadinessSourcesAsync(readiness, f.HostId), flow).State.ShouldBe(current);
    }

    public enum ReadinessStaleResult
    {
        Offline,
        Unavailable,
        OlderStream,
    }

    private static TwitchConfiguredSourceReadiness Configured(
        TwitchEventSourceReadinessOutcome.Available available,
        AutomationFlowId flow
    ) =>
        available
            .Sources.SelectMany(static source => source.Configurations)
            .Single(source => source.FlowId == flow);

    private static Task ObserveReadinessAsync(
        RuntimeFixture f,
        ExpandedAutomationRuntime runtime,
        bool chat,
        string messageId
    ) =>
        chat
            ? runtime.ChatReceivedAsync(
                ReadinessChat(f, messageId, "hello") with
                {
                    ViewerId = messageId,
                },
                CancellationToken.None
            )
            : runtime.TickAsync(CancellationToken.None);

    private static HostStreamLivenessOutcome.Unavailable ReadinessProviderUnavailable() =>
        new(HostStreamLivenessUnavailableReason.ProviderRequestFailed, new HttpRequestException());

    private static HeldReadinessFixture HeldReadinessFor(RuntimeFixture f)
    {
        var streams = new HeldReadinessStreams();
        var connection = new ExpandedConnection();
        var tokens = new ExpandedTokens();
        return new(
            new(
                f.Database,
                f.Runtime,
                f.Countdowns,
                f.Clock,
                streams,
                new ExpandedAccounts(),
                tokens,
                new HelixClient(new ExpandedHttp(), TwitchEndpointPolicy.Default),
                BotSettings.FromOptions(new BotOptions()),
                connection,
                NullLogger<ExpandedAutomationRuntime>.Instance,
                uiChanges: f.UiChanges
            ),
            streams,
            connection,
            tokens
        );
    }

    private sealed record HeldReadinessFixture(
        ExpandedAutomationRuntime Runtime,
        HeldReadinessStreams Streams,
        ExpandedConnection Connection,
        ExpandedTokens Tokens
    );

    private sealed class HeldReadinessObservation
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<HostStreamLivenessOutcome> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class HeldReadinessStreams : IHostStreamLivenessProvider
    {
        private HeldReadinessObservation? _next;

        internal HostStreamLivenessOutcome Current { get; set; } =
            new HostStreamLivenessOutcome.Offline();

        internal HeldReadinessObservation HoldNext() => _next = new();

        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string login) =>
            IO<HostStreamLivenessOutcome, Never>.Create(async ct =>
            {
                var held = Interlocked.Exchange(ref _next, null);
                if (held is null)
                {
                    return Result<HostStreamLivenessOutcome, Never>.Success(Current);
                }
                _ = held.Started.TrySetResult();
                return Result<HostStreamLivenessOutcome, Never>.Success(
                    await held.Release.Task.WaitAsync(ct)
                );
            });
    }
}
