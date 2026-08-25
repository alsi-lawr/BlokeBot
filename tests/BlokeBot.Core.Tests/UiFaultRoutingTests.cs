using BlokeBot.Core.Components;
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
