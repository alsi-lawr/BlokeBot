using System.Buffers.Binary;
using System.Text;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginWorkerBoundaryTests
{
    [Test]
    public async Task CommandInvocation_RoundTripsThroughTypedWorkerProtocol()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            """
            return {
              command = function() return "command" end,
            }
            """
        );
        await using var worker = await package.StartAsync(PluginWorkerMode.Admitted);

        await AssertReturnedAsync(
            worker.Client.InvokeAsync(
                Identity(package),
                new PluginLiveInvocation.Command(Module(), Operation("command"), Nil()),
                CancellationToken.None
            ),
            "command"
        );
    }

    [Test]
    public async Task StagingWorker_RejectsLiveWorkBeforeLuaExecution()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            """
            return {
              prepare = function() return "prepared" end,
              live = function()
                local file = assert(io.open("live-work-ran", "w"))
                file:write("ran")
                file:close()
                return "live"
              end
            }
            """
        );
        await using var worker = await package.StartAsync(PluginWorkerMode.Staging);

        var rejected = await worker.Client.InvokeAsync(
            Identity(package),
            new PluginLiveInvocation.Command(Module(), Operation("live"), Nil()),
            CancellationToken.None
        );
        var prepared = worker.Client.PrepareAsync(
            Identity(package),
            new(Module(), Operation("prepare"), Nil()),
            CancellationToken.None
        );

        rejected
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Failed>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.StagingLiveWorkRejected);
        File.Exists(Path.Combine(worker.StateRoot, "live-work-ran")).ShouldBeFalse();
        await AssertReturnedAsync(prepared, "prepared");
    }

    [Test]
    public async Task PreCancelledAndExpiredCalls_DoNotExecuteLuaSideEffects()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            """
            return { run = function()
              local file = assert(io.open("pre-admission-work-ran", "a"))
              file:write("ran")
              file:close()
              return true
            end }
            """
        );
        await using var worker = await package.StartAsync(PluginWorkerMode.Admitted);
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        var cancelled = await worker.Client.InvokeAsync(
            Identity(package),
            new PluginLiveInvocation.Command(Module(), Operation("run"), Nil()),
            callerCancellation.Token
        );
        var expired = await worker.Client.InvokeAsync(
            Identity(package, TimeSpan.FromSeconds(-1)),
            new PluginLiveInvocation.Command(Module(), Operation("run"), Nil()),
            CancellationToken.None
        );

        cancelled
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Cancelled>()
            .Reason.ShouldBe(PluginCancellationReason.CallerRequested);
        expired
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Cancelled>()
            .Reason.ShouldBe(PluginCancellationReason.DeadlineExceeded);
        File.Exists(Path.Combine(worker.StateRoot, "pre-admission-work-ran")).ShouldBeFalse();
    }

    [Test]
    public async Task OversizedOutgoingValue_IsRejectedBeforeWorkerExecution()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            """
            return {
              run = function()
                local file = assert(io.open("oversized-work-ran", "w"))
                file:write("ran")
                file:close()
                return true
              end,
              alive = function() return 42 end
            }
            """
        );
        await using var worker = await package.StartAsync(PluginWorkerMode.Admitted);
        var oversized = new PluginValue.String(
            new string('x', PluginContractLimits.MaximumPluginValueStringBytes + 1)
        );

        var rejected = await worker.Client.InvokeAsync(
            Identity(package),
            new PluginLiveInvocation.Command(Module(), Operation("run"), oversized),
            CancellationToken.None
        );

        rejected
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Failed>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.InvalidValue);
        File.Exists(Path.Combine(worker.StateRoot, "oversized-work-ran")).ShouldBeFalse();
        await AssertReturnedNumberAsync(
            worker.Client.InvokeAsync(
                Identity(package),
                new PluginLiveInvocation.Command(Module(), Operation("alive"), Nil()),
                CancellationToken.None
            ),
            42
        );
    }

    [Test]
    public async Task InvocationConcurrency_RejectsSecondCallWithoutQueueing()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            "return { run = function() return blokebot.host.call('chat', 'send-message', 'one') end }"
        );
        var dispatcher = new BlockingTestDispatcher();
        await using var worker = await package.StartAsync(PluginWorkerMode.Admitted, dispatcher);
        var first = worker
            .Client.InvokeAsync(
                Identity(package),
                new PluginLiveInvocation.Command(Module(), Operation("run"), Nil()),
                CancellationToken.None
            )
            .AsTask();
        await dispatcher.Started.Task;

        var second = await worker.Client.InvokeAsync(
            Identity(package),
            new PluginLiveInvocation.Event(Module(), Operation("run"), Nil()),
            CancellationToken.None
        );
        _ = dispatcher.Release.TrySetResult();
        var completed = await first;

        second
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Failed>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.InvocationLimitExceeded);
        _ = completed.Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>();
    }

    [Test]
    public async Task DeepLuaValue_ReturnsBoundedTypedFailure()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            """
            return { run = function()
              local value = {}
              for _ = 1, 32 do value = { value } end
              return value
            end }
            """
        );
        await using var worker = await package.StartAsync(PluginWorkerMode.Admitted);

        var result = await worker.Client.InvokeAsync(
            Identity(package),
            new PluginLiveInvocation.Command(Module(), Operation("run"), Nil()),
            CancellationToken.None
        );

        result
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Failed>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.InvalidValue);
    }

    [Test]
    public async Task NonCooperativeDeadline_KillsWorkerAndHostStartsReplacement()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            """
            return {
              block = function()
                local windows = package.config:sub(1, 1) == "\\"
                os.execute(windows and "ping -n 31 127.0.0.1 >NUL" or "sleep 30")
                return 1
              end,
              run = function() return 42 end
            }
            """
        );
        await using (var blocked = await package.StartAsync(PluginWorkerMode.Admitted))
        {
            var result = await blocked.Client.InvokeAsync(
                Identity(package, TimeSpan.FromMilliseconds(200)),
                new PluginLiveInvocation.Command(Module(), Operation("block"), Nil()),
                CancellationToken.None
            );

            result
                .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Cancelled>()
                .ShouldSatisfyAllConditions(
                    outcome => outcome.Reason.ShouldBe(PluginCancellationReason.DeadlineExceeded),
                    outcome => outcome.WorkerTerminated.ShouldBeTrue()
                );
        }

        await using var replacement = await package.StartAsync(PluginWorkerMode.Admitted);
        await AssertReturnedNumberAsync(
            replacement.Client.InvokeAsync(
                Identity(package),
                new PluginLiveInvocation.Command(Module(), Operation("run"), Nil()),
                CancellationToken.None
            ),
            42
        );
    }

    [Test]
    public async Task DiagnosticFlood_TerminatesWorkerWithBoundedFailure()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            """
            return { run = function()
              for _ = 1, 20000 do print(string.rep("x", 100)) end
              return 1
            end }
            """
        );
        await using var worker = await package.StartAsync(PluginWorkerMode.Admitted);

        var result = await worker.Client.InvokeAsync(
            Identity(package),
            new PluginLiveInvocation.Command(Module(), Operation("run"), Nil()),
            CancellationToken.None
        );

        result
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Failed>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.OutputLimitExceeded);
    }

    [Test]
    public async Task WorkerExitAndSubsequentWrite_ReturnTypedFailuresWithoutExitingHost()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            "return { run = function() os.exit(23) end }"
        );
        await using var worker = await package.StartAsync(PluginWorkerMode.Admitted);

        var exit = await worker.Client.InvokeAsync(
            Identity(package),
            new PluginLiveInvocation.Command(Module(), Operation("run"), Nil()),
            CancellationToken.None
        );
        var disconnectedWrite = await worker.Client.InvokeAsync(
            Identity(package, TimeSpan.FromSeconds(1)),
            new PluginLiveInvocation.Command(Module(), Operation("run"), Nil()),
            CancellationToken.None
        );

        exit.Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Failed>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.WorkerExited);
        disconnectedWrite
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Failed>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.WorkerExited);
    }

    [Test]
    public async Task Coordinator_AllowsOneAdmittedAndOneStagingWorkerPerPlugin()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            "return { prepare = function() return true end }"
        );
        await using var coordinator = new PluginWorkerCoordinator();
        var admitted = await coordinator.StartAsync(
            package.StartOptions(PluginWorkerMode.Admitted, "admitted"),
            CancellationToken.None
        );
        var staging = await coordinator.StartAsync(
            package.StartOptions(PluginWorkerMode.Staging, "staging"),
            CancellationToken.None
        );

        var duplicateAdmitted = await coordinator.StartAsync(
            package.StartOptions(PluginWorkerMode.Admitted, "duplicate-admitted"),
            CancellationToken.None
        );
        var duplicateStaging = await coordinator.StartAsync(
            package.StartOptions(PluginWorkerMode.Staging, "duplicate-staging"),
            CancellationToken.None
        );

        await using var admittedLease = admitted
            .ShouldBeOfType<PluginWorkerReservationOutcome.Started>()
            .Lease;
        await using var stagingLease = staging
            .ShouldBeOfType<PluginWorkerReservationOutcome.Started>()
            .Lease;
        duplicateAdmitted
            .ShouldBeOfType<PluginWorkerReservationOutcome.Rejected>()
            .Failure.Code.ShouldBe(PluginWorkerReservationFailureCode.AdmittedWorkerExists);
        duplicateStaging
            .ShouldBeOfType<PluginWorkerReservationOutcome.Rejected>()
            .Failure.Code.ShouldBe(PluginWorkerReservationFailureCode.StagingWorkerExists);
    }

    [Test]
    public async Task Coordinator_CancelledStartReleasesWorkerReservation()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            "return { run = function() return true end }"
        );
        await using var coordinator = new PluginWorkerCoordinator();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        _ = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await coordinator.StartAsync(
                package.StartOptions(PluginWorkerMode.Admitted, "cancelled-start"),
                cancellation.Token
            )
        );
        var retry = await coordinator.StartAsync(
            package.StartOptions(PluginWorkerMode.Admitted, "retry"),
            CancellationToken.None
        );

        await using var lease = retry
            .ShouldBeOfType<PluginWorkerReservationOutcome.Started>()
            .Lease;
    }

    [Test]
    public async Task PackageMaterializer_PreservesReviewedPayloadBytes()
    {
        await using var package = await MaterializedPluginTestPackage.CreateAsync(
            "return { run = function() return 1 end }"
        );
        var expected = PluginContractFixtures
            .CompletePackage()
            .OfType<PluginPackageEntry.File>()
            .Where(file => file.Path.StartsWith("payloads/", StringComparison.Ordinal))
            .ToDictionary(file => file.Path, file => file.Content.ToArray());

        foreach (var (relativePath, bytes) in expected)
        {
            var materialized = await File.ReadAllBytesAsync(
                Path.Combine(
                    package.Package.PackageRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)
                )
            );
            materialized.ShouldBe(bytes);
        }
    }

    [Test]
    public async Task ProtocolCodec_RejectsOversizedDuplicateAndUnknownFrames()
    {
        var codec = new PluginWorkerProtocolCodec();
        var oversized = new MemoryStream();
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(header, PluginWorkerLimits.MaximumFrameBytes + 1);
        await oversized.WriteAsync(header);
        oversized.Position = 0;

        var oversizedResult = await codec.ReadAsync(oversized, CancellationToken.None);
        var duplicateResult = await codec.ReadAsync(
            Framed("{\"message\":\"stop\",\"message\":\"stop\"}"),
            CancellationToken.None
        );
        var unknownResult = await codec.ReadAsync(
            Framed("{\"message\":\"stop\",\"unknown\":true}"),
            CancellationToken.None
        );

        oversizedResult
            .ShouldBeOfType<PluginFrameReadOutcome.Rejected>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.FrameTooLarge);
        duplicateResult
            .ShouldBeOfType<PluginFrameReadOutcome.Rejected>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.MalformedFrame);
        unknownResult
            .ShouldBeOfType<PluginFrameReadOutcome.Rejected>()
            .Failure.Code.ShouldBe(PluginWorkerFailureCode.MalformedFrame);
    }

    private static async ValueTask AssertReturnedAsync(
        ValueTask<PluginWorkerInvocationResult> result,
        string expected
    ) =>
        (await result)
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>()
            .Value.ShouldBe(new PluginValue.String(expected));

    private static async ValueTask AssertReturnedNumberAsync(
        ValueTask<PluginWorkerInvocationResult> result,
        double expected
    ) =>
        (await result)
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>()
            .Value.ShouldBe(new PluginValue.Number(expected));

    private static PluginWorkerInvocationIdentity Identity(
        MaterializedPluginTestPackage package,
        TimeSpan? duration = null
    ) => MaterializedPluginTestPackage.Identity(package.Package.Descriptor.Plugin, duration);

    private static PluginLuaModuleId Module() => MaterializedPluginTestPackage.ModuleId();

    private static PluginHostOperationId Operation(string value) =>
        MaterializedPluginTestPackage.OperationId(value);

    private static PluginValue Nil() => new PluginValue.Nil();

    private static MemoryStream Framed(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var bytes = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(bytes, payload.Length);
        payload.CopyTo(bytes.AsSpan(sizeof(int)));
        return new(bytes);
    }

    private sealed class BlockingTestDispatcher : IPluginHostCallDispatcher
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        )
        {
            _ = Started.TrySetResult();
            await Release.Task;
            return new PluginHostCallOutcome.Returned(new PluginValue.Nil());
        }
    }
}
