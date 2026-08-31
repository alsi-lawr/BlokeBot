namespace BlokeBot.Plugins.Contracts.Testing;

public static class PluginEngineFixturePrograms
{
    public const string ValueMapping =
        "return { enabled = true, count = 42, label = 'mapped', items = { 1, 2, 3 } }";

    public const string FullStandardLibrary =
        "assert(string.upper('lua') == 'LUA'); "
        + "assert(table.concat({'a', 'b'}, ',') == 'a,b'); "
        + "assert(math.max(3, 7) == 7); "
        + "local path = os.tmpname(); local output = assert(io.open(path, 'w')); "
        + "output:write('fixture'); output:close(); "
        + "local input = assert(io.open(path, 'r')); assert(input:read('*a') == 'fixture'); "
        + "input:close(); os.remove(path); "
        + "assert(type(os.time()) == 'number'); "
        + "assert(type(os.execute) == 'function'); "
        + "assert(type(debug.getinfo(1)) == 'table'); "
        + "assert(type(package.searchers) == 'table'); "
        + "local thread = coroutine.create(function() coroutine.yield(42) end); "
        + "local ok, value = coroutine.resume(thread); assert(ok and value == 42); "
        + "assert(utf8.char(0x41) == 'A'); return 'full-standard-library-ok'";

    public const string Coroutine =
        "local blokebot = require('blokebot'); return blokebot.chat.send('fixture message')";

    public const string Cancellation =
        "local blokebot = require('blokebot'); return blokebot.chat.send('cancel after external effect')";

    public const string Packaging = "local module = require('events'); return module.answer";
}

public interface IPluginEngineContractFixtureAdapter
{
    PluginEngineDescriptor Descriptor { get; }

    ValueTask<PluginValue> RoundTripValueAsync(
        string program,
        PluginValue expectedValue,
        CancellationToken cancellationToken
    );

    ValueTask<PluginValue> ExecuteStandardLibraryAsync(
        string program,
        CancellationToken cancellationToken
    );

    ValueTask<PluginCoroutineFixtureObservation> ExecuteCoroutineAsync(
        string program,
        PluginHostCall call,
        PluginHostCallCompletion completion,
        CancellationToken cancellationToken
    );

    ValueTask<PluginCancellationFixtureObservation> ExecuteCancellationAsync(
        string program,
        PluginHostCall call,
        PluginHostCallCancellation cancellation,
        CancellationToken cancellationToken
    );

    ValueTask<PluginValue> ExecutePackageAsync(
        string program,
        IReadOnlyList<PluginPackageEntry> package,
        CancellationToken cancellationToken
    );
}

public sealed record PluginCoroutineFixtureObservation(
    PluginCoroutineId SuspendedCoroutineId,
    PluginHostCallOutcome Outcome,
    int ResumeCount
);

public sealed record PluginCancellationFixtureObservation(
    PluginCoroutineId SuspendedCoroutineId,
    PluginHostCallOutcome Outcome,
    int ResumeCount,
    PluginCancellationLateResultState LateResult,
    PluginCancellationExternalEffectState ExternalEffect
);

public enum PluginCancellationLateResultState
{
    Discarded,
    Admitted,
}

public enum PluginCancellationExternalEffectState
{
    RemainedCompleted,
    RolledBack,
}

public enum PluginEngineFixtureFailureCode
{
    IncompatibleEngineDescriptor,
    MappedValueInvalid,
    ValueMappingFailed,
    StandardLibraryFailed,
    CoroutineFailed,
    CancellationFailed,
    PackagingFailed,
}

public sealed record PluginEngineFixtureFailure(PluginEngineFixtureFailureCode Code);

public abstract record PluginEngineFixtureOutcome
{
    private PluginEngineFixtureOutcome() { }

    public sealed record Passed : PluginEngineFixtureOutcome;

    public sealed record Failed(IReadOnlyList<PluginEngineFixtureFailure> Failures)
        : PluginEngineFixtureOutcome;
}

public static class PluginEngineContractFixtures
{
    public static async ValueTask<PluginEngineFixtureOutcome> RunAsync(
        IPluginEngineContractFixtureAdapter adapter,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var failures = new List<PluginEngineFixtureFailure>();
        if (
            PluginCompatibilityEvaluator.AdmitEngine(adapter.Descriptor)
            is PluginEngineAdmissionOutcome.Rejected
        )
        {
            failures.Add(new(PluginEngineFixtureFailureCode.IncompatibleEngineDescriptor));
        }

        var expectedValue = CompleteValue();
        var mapped = await adapter.RoundTripValueAsync(
            PluginEngineFixturePrograms.ValueMapping,
            expectedValue,
            cancellationToken
        );
        if (
            mapped is null
            || PluginValueValidator.Validate(mapped) is PluginValueValidationOutcome.Invalid
        )
        {
            failures.Add(new(PluginEngineFixtureFailureCode.MappedValueInvalid));
        }
        else if (!PluginValueComparer.SemanticallyEquals(expectedValue, mapped))
        {
            failures.Add(new(PluginEngineFixtureFailureCode.ValueMappingFailed));
        }

        var standardLibrary = await adapter.ExecuteStandardLibraryAsync(
            PluginEngineFixturePrograms.FullStandardLibrary,
            cancellationToken
        );
        if (standardLibrary is not PluginValue.String { Value: "full-standard-library-ok" })
        {
            failures.Add(new(PluginEngineFixtureFailureCode.StandardLibraryFailed));
        }

        var call = HostCall("fixture message");
        var returned = new PluginHostCallOutcome.Returned(new PluginValue.Nil());
        var completion = new PluginHostCallCompletion(call.CallId, call.CoroutineId, returned);
        var coroutine = await adapter.ExecuteCoroutineAsync(
            PluginEngineFixturePrograms.Coroutine,
            call,
            completion,
            cancellationToken
        );
        if (
            coroutine.SuspendedCoroutineId != call.CoroutineId
            || coroutine.ResumeCount != 1
            || coroutine.Outcome is not PluginHostCallOutcome.Returned
        )
        {
            failures.Add(new(PluginEngineFixtureFailureCode.CoroutineFailed));
        }

        var cancellationCall = HostCall("cancel after external effect");
        var cancellation = new PluginHostCallCancellation(
            cancellationCall.CallId,
            cancellationCall.CoroutineId,
            PluginCancellationReason.CallerRequested
        );
        var cancelled = await adapter.ExecuteCancellationAsync(
            PluginEngineFixturePrograms.Cancellation,
            cancellationCall,
            cancellation,
            cancellationToken
        );
        if (
            cancelled.SuspendedCoroutineId != cancellationCall.CoroutineId
            || cancelled.ResumeCount != 1
            || cancelled.LateResult != PluginCancellationLateResultState.Discarded
            || cancelled.ExternalEffect != PluginCancellationExternalEffectState.RemainedCompleted
            || cancelled.Outcome
                is not PluginHostCallOutcome.Cancelled
                {
                    Reason: PluginCancellationReason.CallerRequested,
                }
        )
        {
            failures.Add(new(PluginEngineFixtureFailureCode.CancellationFailed));
        }

        var packaged = await adapter.ExecutePackageAsync(
            PluginEngineFixturePrograms.Packaging,
            PluginContractFixtures.CompletePackage(),
            cancellationToken
        );
        if (packaged is not PluginValue.Number { Value: 42 })
        {
            failures.Add(new(PluginEngineFixtureFailureCode.PackagingFailed));
        }

        return failures.Count == 0
            ? new PluginEngineFixtureOutcome.Passed()
            : new PluginEngineFixtureOutcome.Failed(failures.AsReadOnly());
    }

    private static PluginValue CompleteValue() =>
        new PluginValue.Map([
            new("enabled", new PluginValue.Boolean(true)),
            new("count", new PluginValue.Number(42)),
            new("label", new PluginValue.String("mapped")),
            new(
                "items",
                new PluginValue.Array([
                    new PluginValue.Number(1),
                    new PluginValue.Number(2),
                    new PluginValue.Number(3),
                ])
            ),
        ]);

    private static PluginHostCall HostCall(string message)
    {
        var plugin = PluginContractFixtures.PluginId("community.link-queue");
        var version = PluginContractFixtures.SemanticVersion("1.2.0");
        var tag = GitTag("community-link-queue");
        var host = HostId(1);
        return new(
            PluginContractFixtures.HostCallId(),
            PluginContractFixtures.CoroutineId(),
            PluginContractFixtures.HostModuleId("chat"),
            PluginContractFixtures.HostOperationId("send"),
            new PluginInvocationContext.Channel(new(plugin, new(version, tag)), host),
            [new PluginValue.String(message)]
        );
    }

    private static PluginGitTag GitTag(string value) =>
        PluginGitTag.TryCreate(value, out var tag)
            ? tag
            : throw new InvalidOperationException($"Invalid Git tag fixture '{value}'.");

    private static PluginHostId HostId(int value) =>
        PluginHostId.TryCreate(value, out var hostId)
            ? hostId
            : throw new InvalidOperationException($"Invalid host ID fixture '{value}'.");
}
