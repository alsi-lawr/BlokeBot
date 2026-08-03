using BlokeBot.Core.Components;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class UiFaultRoutingTests
{
    [Test]
    public void UnavailableReadiness_Mapping_ReturnsTypedExpectedLoadFailure()
    {
        var result = HostBotChannelStatusLoadFailure.FromReadiness(
            new HostBotReadinessOutcome.Unknown(new(true, false, false, false))
        );

        var failure = result.Match<HostBotChannelStatusLoadFailure?>(
            static _ => null,
            static error => error
        );

        _ = failure.ShouldNotBeNull();
        failure.ModeratorStatusMessage.ShouldBe(
            "BlokeBot could not check whether the bot is a mod."
        );
        failure.FollowerReadStatusMessage.ShouldBe(
            "BlokeBot could not check follower-only giveaways."
        );
    }

    [Test]
    public void UnexpectedFault_Reporting_EmitsSafeContextWithoutExceptionDetails()
    {
        var logger = new RecordingLogger<UiFaultTelemetry>();
        var telemetry = new UiFaultTelemetry(logger);
        const string SensitiveMessage = "secret-token-from-sensitive-failure";
        var exception = new InvalidOperationException(SensitiveMessage);

        telemetry.Report(
            exception,
            new UiFaultContext("PointsConfigurationPage", "SaveAsync", 42, null)
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Properties["UiComponent"].ShouldBe("PointsConfigurationPage");
        entry.Properties["UiOperation"].ShouldBe("SaveAsync");
        entry.Properties["HostId"].ShouldBe(42);
        entry.Properties["FailureType"].ShouldBe(typeof(InvalidOperationException).FullName);
        entry.Message.ShouldNotContain(SensitiveMessage);
        entry.Properties.Values.ShouldAllBe(static value =>
            value == null || !value.ToString()!.Contains(SensitiveMessage, StringComparison.Ordinal)
        );
    }

    [Test]
    public void ExpectedBackgroundFailure_Completing_RendersTypedStateWithoutTelemetry()
    {
        using var context = new BunitContext();
        var logger = new RecordingLogger<UiFaultTelemetry>();
        _ = context.Services.AddSingleton(new UiFaultTelemetry(logger));
        var expected = new TestExpectedFailure("not available");

        var component = context.Render<TestBackgroundComponent>(parameters =>
            parameters
                .Add(x => x.Identity, new TestLoadIdentity("first"))
                .Add(
                    x => x.Loader,
                    _ => Task.FromResult(Result<string, TestExpectedFailure>.Error(expected))
                )
        );

        component.WaitForAssertion(() =>
        {
            component.Instance.Error.ShouldBeSameAs(expected);
            component.Instance.Value.ShouldBeNull();
            component.Instance.Loading.ShouldBeFalse();
        });
        logger.Entries.ShouldBeEmpty();
    }

    [Test]
    public void UnexpectedBackgroundFault_Completing_ReportsAndReachesErrorBoundary()
    {
        using var context = new BunitContext();
        var logger = new RecordingLogger<UiFaultTelemetry>();
        _ = context.Services.AddSingleton(new UiFaultTelemetry(logger));
        var exception = new InvalidOperationException("unexpected");
        RenderFragment content = builder =>
        {
            builder.OpenComponent<TestBackgroundComponent>(0);
            builder.AddAttribute(
                1,
                nameof(TestBackgroundComponent.Identity),
                new TestLoadIdentity("first")
            );
            builder.AddAttribute(
                2,
                nameof(TestBackgroundComponent.Loader),
                (Func<CancellationToken, Task<Result<string, TestExpectedFailure>>>)(
                    _ => Task.FromException<Result<string, TestExpectedFailure>>(exception)
                )
            );
            builder.CloseComponent();
        };
        var boundary = context.Render<CapturingErrorBoundary>(parameters =>
            parameters.Add(x => x.ChildContent, content)
        );

        boundary.WaitForAssertion(() =>
            boundary.Instance.CapturedException.ShouldBeSameAs(exception)
        );
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.Properties["UiComponent"].ShouldBe(nameof(TestBackgroundComponent));
        entry.Properties["UiOperation"].ShouldBe("LoadBackgroundValueAsync");
        entry.Properties["LoadIdentityType"].ShouldBe(nameof(TestLoadIdentity));
    }

    [Test]
    public async Task SupersededBackgroundLoad_Cancelling_RemainsSilentAndAppliesLatestValue()
    {
        using var context = new BunitContext();
        var logger = new RecordingLogger<UiFaultTelemetry>();
        _ = context.Services.AddSingleton(new UiFaultTelemetry(logger));
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var loaderStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var component = context.Render<TestBackgroundComponent>(parameters =>
            parameters
                .Add(x => x.Identity, new TestLoadIdentity("first"))
                .Add(
                    x => x.Loader,
                    async ct =>
                    {
                        using var registration = ct.Register(cancellationObserved.SetResult);
                        loaderStarted.SetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                        return Result<string, TestExpectedFailure>.Success("first");
                    }
                )
        );

        await loaderStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        component.Render(parameters =>
            parameters
                .Add(x => x.Identity, new TestLoadIdentity("second"))
                .Add(
                    x => x.Loader,
                    _ => Task.FromResult(Result<string, TestExpectedFailure>.Success("second"))
                )
        );

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        component.WaitForAssertion(() =>
        {
            component.Instance.Value.ShouldBe("second");
            component.Instance.Error.ShouldBeNull();
            component.Instance.Loading.ShouldBeFalse();
        });
        logger.Entries.ShouldBeEmpty();
    }

    public sealed class TestBackgroundComponent
        : BackgroundLoadComponent<string, TestExpectedFailure, TestLoadIdentity>
    {
        [Parameter]
        public TestLoadIdentity? Identity { get; set; }

        [Parameter]
        public Func<
            CancellationToken,
            Task<Result<string, TestExpectedFailure>>
        > Loader { get; set; } =
            static _ => Task.FromResult(Result<string, TestExpectedFailure>.Success(string.Empty));

        public TestExpectedFailure? Error => BackgroundError;

        public string? Value => BackgroundValue;

        public bool Loading => IsBackgroundLoading;

        protected override TestLoadIdentity? BackgroundLoadIdentity => Identity;

        protected override Task<Result<string, TestExpectedFailure>> LoadBackgroundValueAsync(
            CancellationToken ct
        ) => Loader(ct);
    }

    public sealed record TestExpectedFailure(string Message);

    public sealed record TestLoadIdentity(string Value);
}
