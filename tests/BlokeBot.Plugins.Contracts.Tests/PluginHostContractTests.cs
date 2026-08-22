using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginHostContractTests
{
    [Test]
    public void PluginValuesAndHostCalls_EnforceTypedPayloadBounds()
    {
        var duplicateMap = new PluginValue.Map([
            new("duplicate", new PluginValue.Boolean(true)),
            new("duplicate", new PluginValue.Boolean(false)),
        ]);
        var oversized = new PluginValue.String(
            new('x', PluginContractLimits.MaximumPluginValueStringBytes + 1)
        );
        var invalidNumber = new PluginValue.Number(double.NaN);
        PluginValue deeplyNested = new PluginValue.Nil();
        for (var depth = 0; depth < PluginContractLimits.MaximumPluginValueDepth; depth++)
        {
            deeplyNested = new PluginValue.Array([deeplyNested]);
        }
        var orderedMap = new PluginValue.Map([
            new("first", new PluginValue.Boolean(true)),
            new("second", new PluginValue.Number(2)),
        ]);
        var reversedMap = new PluginValue.Map([.. orderedMap.Properties.Reverse()]);

        ValueErrorCodes(duplicateMap).ShouldContain(PluginValueErrorCode.DuplicateMapKey);
        ValueErrorCodes(oversized).ShouldContain(PluginValueErrorCode.StringTooLarge);
        ValueErrorCodes(invalidNumber).ShouldContain(PluginValueErrorCode.NonFiniteNumber);
        ValueErrorCodes(deeplyNested).ShouldContain(PluginValueErrorCode.DepthExceeded);
        PluginValueComparer.SemanticallyEquals(orderedMap, reversedMap).ShouldBeTrue();

        var module = PluginContractFixtures.CompatibleHost().HostModules.ShouldHaveSingleItem();
        var call = HostCall(new PluginInvocationContext.Installation(InstallationIdentity()));
        var callErrors = PluginHostCallValidator
            .ValidateCall(call, module)
            .ShouldBeOfType<PluginHostCallValidationOutcome.Invalid>()
            .Errors;

        callErrors
            .Select(error => error.Code)
            .ShouldContain(PluginHostCallErrorCode.ContextNotPermitted);
    }

    [Test]
    public void SemanticVersions_ApplyPrereleasePrecedenceAndRejectAmbiguousForms()
    {
        var alphaTen = PluginContractFixtures.SemanticVersion("1.0.0-alpha.10");
        var alphaEleven = PluginContractFixtures.SemanticVersion("1.0.0-alpha.11");
        var largeNumeric = PluginContractFixtures.SemanticVersion(
            "1.0.0-alpha.99999999999999999999"
        );
        var release = PluginContractFixtures.SemanticVersion("1.0.0");

        (alphaTen < alphaEleven).ShouldBeTrue();
        (alphaEleven < largeNumeric).ShouldBeTrue();
        (largeNumeric < release).ShouldBeTrue();
        SemanticVersion.TryCreate("01.0.0", out _).ShouldBeFalse();
        SemanticVersion.TryCreate("1.0", out _).ShouldBeFalse();
    }

    [Test]
    public void CompatibilityAdmission_RequiresTrustedLua54Contract()
    {
        var compatible = PluginContractFixtures.CompatibleEngine();
        var restricted = compatible with
        {
            StandardLibrary = PluginStandardLibrary.Restricted,
            SupportsCoroutines = false,
        };

        var accepted = PluginCompatibilityEvaluator
            .AdmitEngine(compatible)
            .ShouldBeOfType<PluginEngineAdmissionOutcome.Accepted>();
        var rejected = PluginCompatibilityEvaluator
            .AdmitEngine(restricted)
            .ShouldBeOfType<PluginEngineAdmissionOutcome.Rejected>();

        accepted.Trust.TrustLevel.ShouldBe(PluginTrustLevel.FullyTrusted);
        accepted.Trust.OperatingSystemAccess.ShouldBe(PluginOperatingSystemAccess.BlokeBotAccount);
        accepted.Trust.ProcessIsolation.ShouldBe(PluginProcessIsolationBoundary.AvailabilityOnly);
        accepted.Trust.StandardLibrary.ShouldBe(PluginStandardLibrary.Full);
        rejected
            .Failures.ShouldHaveSingleItem()
            .Code.ShouldBe(PluginCompatibilityFailureCode.IncompatibleEngine);
    }

    [Test]
    public async Task EngineContractFixture_RejectsBrokenCancellationSemantics()
    {
        var adapter = new FixtureEngineAdapter(breakCancellation: true);

        var outcome = await PluginEngineContractFixtures.RunAsync(adapter, CancellationToken.None);

        outcome
            .ShouldBeOfType<PluginEngineFixtureOutcome.Failed>()
            .Failures.ShouldHaveSingleItem()
            .Code.ShouldBe(PluginEngineFixtureFailureCode.CancellationFailed);
    }

    private static IReadOnlyList<PluginValueErrorCode> ValueErrorCodes(PluginValue value) =>
        PluginValueValidator
            .Validate(value)
            .ShouldBeOfType<PluginValueValidationOutcome.Invalid>()
            .Errors.Select(error => error.Code)
            .ToArray();

    private static PluginHostCall HostCall(PluginInvocationContext context) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PluginContractFixtures.HostModuleId("chat"),
            PluginContractFixtures.HostOperationId("send-message"),
            context,
            [new PluginValue.String("hello")]
        );

    private static PluginInstallationIdentity InstallationIdentity()
    {
        PluginGitTag.TryCreate("community-link-queue", out var tag).ShouldBeTrue();
        return new(
            PluginContractFixtures.PluginId("community.link-queue"),
            new(PluginContractFixtures.SemanticVersion("1.2.0"), tag)
        );
    }

    private sealed class FixtureEngineAdapter(bool breakCancellation)
        : IPluginEngineContractFixtureAdapter
    {
        public PluginEngineDescriptor Descriptor { get; } =
            PluginContractFixtures.CompatibleEngine();

        public ValueTask<PluginValue> RoundTripValueAsync(
            string program,
            PluginValue expectedValue,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(expectedValue);

        public ValueTask<PluginValue> ExecuteStandardLibraryAsync(
            string program,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PluginValue>(new PluginValue.String("full-standard-library-ok"));

        public ValueTask<PluginCoroutineFixtureObservation> ExecuteCoroutineAsync(
            string program,
            PluginHostCall call,
            PluginHostCallCompletion completion,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult(
                new PluginCoroutineFixtureObservation(
                    call.CoroutineId,
                    completion.Outcome,
                    ResumeCount: 1
                )
            );

        public ValueTask<PluginCancellationFixtureObservation> ExecuteCancellationAsync(
            string program,
            PluginHostCall call,
            PluginHostCallCancellation cancellation,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult(
                new PluginCancellationFixtureObservation(
                    call.CoroutineId,
                    breakCancellation
                        ? new PluginHostCallOutcome.Returned(new PluginValue.Nil())
                        : new PluginHostCallOutcome.Cancelled(cancellation.Reason),
                    ResumeCount: 1
                )
            );

        public ValueTask<PluginValue> ExecutePackageAsync(
            string program,
            IReadOnlyList<PluginPackageEntry> package,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PluginValue>(new PluginValue.Number(42));
    }
}
