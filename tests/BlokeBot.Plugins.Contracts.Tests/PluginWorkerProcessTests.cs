using System.Diagnostics;
using System.Text;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginWorkerProcessTests
{
    [Test]
    public async Task KeraLuaWorker_PassesCanonicalEngineContract()
    {
        var adapter = new RealWorkerFixtureAdapter();

        var outcome = await PluginEngineContractFixtures.RunAsync(adapter, CancellationToken.None);

        _ = outcome.ShouldBeOfType<PluginEngineFixtureOutcome.Passed>();
    }

    [Test]
    public async Task KeraLuaWorker_PreservesAZeroArgumentHostCall()
    {
        var dispatcher = new CapturingDispatcher(new PluginValue.Nil());
        var execution = RealWorkerFixtureAdapter
            .RunResultAsync(
                "local blokebot = require('blokebot'); return blokebot.context.current()",
                dispatcher,
                PluginContractFixtures.CoroutineId(),
                CancellationToken.None
            )
            .AsTask();

        var call = await dispatcher.Call.Task;
        var result = await execution;

        call.Module.Value.ShouldBe("context");
        call.Operation.Value.ShouldBe("current");
        call.Arguments.ShouldBeEmpty();
        _ = result.Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>();
    }

    [Test]
    public async Task KeraLuaWorker_ExposesOnlyTheVersionedFacade()
    {
        var result = await RealWorkerFixtureAdapter.RunResultAsync(
            "local blokebot = require('blokebot'); return { api = blokebot.api_version, host_private = blokebot.host == nil }",
            new ReturningDispatcher(new PluginValue.Nil()),
            PluginContractFixtures.CoroutineId(),
            CancellationToken.None
        );

        var value = result
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>()
            .Value.ShouldBeOfType<PluginValue.Map>();
        value.Properties.ShouldContain(new PluginValueProperty("api", new PluginValue.Number(1)));
        value.Properties.ShouldContain(
            new PluginValueProperty("host_private", new PluginValue.Boolean(true))
        );
    }

    [Test]
    public async Task KeraLuaWorker_ExposesHostFailuresAsTaggedLuaOutcomes()
    {
        var result = await RealWorkerFixtureAdapter.RunResultAsync(
            "local blokebot = require('blokebot'); local ok, failure = pcall(blokebot.chat.send, 'rejected'); assert(not ok); return failure",
            new OutcomeDispatcher(
                new PluginHostCallOutcome.Failed(
                    new(PluginHostFailureCode.ProviderRejected, "Fixture message was rejected.")
                )
            ),
            PluginContractFixtures.CoroutineId(),
            CancellationToken.None
        );

        var value = result
            .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>()
            .Value.ShouldBeOfType<PluginValue.Map>();
        value.Properties.ShouldContain(
            new PluginValueProperty("kind", new PluginValue.String("failed"))
        );
        value.Properties.ShouldContain(
            new PluginValueProperty("code", new PluginValue.String("providerRejected"))
        );
        value.Properties.ShouldContain(
            new PluginValueProperty(
                "safeMessage",
                new PluginValue.String("Fixture message was rejected.")
            )
        );
    }

    [Test]
    public async Task KeraLuaWorker_ReceivesEveryCanonicalWebAndPageInputField()
    {
        PluginHostId.TryCreate(7, out var hostId).ShouldBeTrue();
        var sessionId = PluginContractFixtures.PageSessionId();
        var inputs = new[]
        {
            (
                Schema: PluginInvocationInputSchemas.Web,
                Value: PluginInvocationInputs.Web(
                    "POST",
                    new Dictionary<string, string> { ["content-type"] = "application/json" },
                    "{}"u8
                )
            ),
            (
                Schema: PluginInvocationInputSchemas.Page,
                Value: PluginInvocationInputs.Page(hostId, sessionId)
            ),
        };

        foreach (var input in inputs)
        {
            var assertions = string.Join(
                ' ',
                input.Schema.Fields.Select(field => $"assert(input[\"{field.Name}\"] ~= nil);")
            );
            var result = await RealWorkerFixtureAdapter.RunResultAsync(
                $"{assertions} return input",
                new ReturningDispatcher(new PluginValue.Nil()),
                PluginContractFixtures.CoroutineId(),
                CancellationToken.None,
                input: input.Value
            );

            var returned = result.Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>();
            PluginValueComparer.SemanticallyEquals(input.Value, returned.Value).ShouldBeTrue();
        }
    }

    [Test]
    public async Task Generate_AddsExecutableDeclaredHandlerSkeletonsWithoutChangingAuthorLua()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-generated-handler-test-{Guid.NewGuid():N}"
        );
        try
        {
            var initialized = await PluginProjectWriter.InitializeAsync(
                PluginContractFixtures.PluginId("examples.generated-handlers"),
                root,
                CancellationToken.None
            );
            _ = initialized.ShouldBeOfType<PluginProjectWriteOutcome.Written>();
            var authorPath = Path.Combine(root, "lua", "main.lua");
            var authorLua = await File.ReadAllBytesAsync(authorPath);
            var manifestPath = Path.Combine(root, PluginPackage.ManifestPath);
            var manifest = await File.ReadAllTextAsync(manifestPath);
            var hostModules = manifest.IndexOf("\n[[hostModules]]", StringComparison.Ordinal);
            await File.WriteAllTextAsync(
                manifestPath,
                manifest.Insert(
                    hostModules,
                    """

                    [[features.dispatch.commands]]
                    route = "generated-handler"
                    module = "main"
                    operation = "handle_generated"
                    [features.dispatch.commands.requirements]
                    twitchReady = false
                    """
                )
            );

            var generated = await PluginProjectWriter.GenerateAsync(root, CancellationToken.None);
            _ = generated.ShouldBeOfType<PluginProjectWriteOutcome.Written>();
            (await File.ReadAllBytesAsync(authorPath)).ShouldBe(authorLua);

            var skeleton = await File.ReadAllBytesAsync(
                Path.Combine(
                    root,
                    PluginProjectArtifacts.GeneratedRoot.Replace('/', Path.DirectorySeparatorChar),
                    PluginProjectArtifacts.GeneratedHandlerSkeleton
                )
            );
            var package = PluginContractFixtures
                .CompletePackage()
                .Select(entry =>
                    entry.Path == "lua/events.lua"
                        ? new PluginPackageEntry.File(entry.Path, skeleton)
                        : entry
                )
                .ToArray();
            var result = await RealWorkerFixtureAdapter.RunResultAsync(
                "local generated = require('events'); return generated['main']['handle_generated']({ marker = 'new declaration' })",
                new ReturningDispatcher(new PluginValue.Nil()),
                PluginContractFixtures.CoroutineId(),
                CancellationToken.None,
                package
            );

            var value = result
                .Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>()
                .Value.ShouldBeOfType<PluginValue.Map>();
            value.Properties.ShouldContain(
                new PluginValueProperty("marker", new PluginValue.String("new declaration"))
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class RealWorkerFixtureAdapter : IPluginEngineContractFixtureAdapter
    {
        public PluginEngineDescriptor Descriptor => PluginWorkerEngineContract.Selected;

        public async ValueTask<PluginValue> RoundTripValueAsync(
            string program,
            PluginValue expectedValue,
            CancellationToken cancellationToken
        ) => await RunAsync(program, new ReturningDispatcher(expectedValue), cancellationToken);

        public async ValueTask<PluginValue> ExecuteStandardLibraryAsync(
            string program,
            CancellationToken cancellationToken
        ) =>
            await RunAsync(
                program,
                new ReturningDispatcher(new PluginValue.Nil()),
                cancellationToken
            );

        public async ValueTask<PluginCoroutineFixtureObservation> ExecuteCoroutineAsync(
            string program,
            PluginHostCall call,
            PluginHostCallCompletion completion,
            CancellationToken cancellationToken
        )
        {
            completion.CallId.ShouldBe(call.CallId);
            completion.CoroutineId.ShouldBe(call.CoroutineId);
            var dispatcher = new CapturingDispatcher(OutcomeValue(completion.Outcome));
            var execution = RunResultAsync(program, dispatcher, call.CoroutineId, cancellationToken)
                .AsTask();
            var actualCall = await dispatcher.Call.Task.WaitAsync(cancellationToken);
            AssertHostCall(call, actualCall);
            var result = await execution;
            return new(
                actualCall.CoroutineId,
                HostCallOutcome(result.Outcome),
                result.Metrics.ResumeCount
            );
        }

        public async ValueTask<PluginCancellationFixtureObservation> ExecuteCancellationAsync(
            string program,
            PluginHostCall call,
            PluginHostCallCancellation cancellation,
            CancellationToken cancellationToken
        )
        {
            cancellation.CallId.ShouldBe(call.CallId);
            cancellation.CoroutineId.ShouldBe(call.CoroutineId);
            var dispatcher = new DelayedEffectDispatcher();
            using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            var execution = RunResultAsync(
                    program,
                    dispatcher,
                    call.CoroutineId,
                    callerCancellation.Token
                )
                .AsTask();
            await dispatcher.EffectCompleted.Task.WaitAsync(cancellationToken);
            var actualCall = await dispatcher.Call.Task.WaitAsync(cancellationToken);
            AssertHostCall(call, actualCall);
            callerCancellation.Cancel();
            var result = await execution;
            _ = dispatcher.ReleaseLateResult.TrySetResult();
            await dispatcher.DispatchCompleted.Task.WaitAsync(cancellationToken);

            return new(
                actualCall.CoroutineId,
                result.Outcome is PluginWorkerInvocationOutcome.Cancelled cancelled
                    ? new PluginHostCallOutcome.Cancelled(cancelled.Reason)
                    : new PluginHostCallOutcome.Returned(new PluginValue.Nil()),
                result.Metrics.ResumeCount,
                PluginCancellationLateResultState.Discarded,
                dispatcher.ExternalEffectCompleted
                    ? PluginCancellationExternalEffectState.RemainedCompleted
                    : PluginCancellationExternalEffectState.RolledBack
            );
        }

        public async ValueTask<PluginValue> ExecutePackageAsync(
            string program,
            IReadOnlyList<PluginPackageEntry> package,
            CancellationToken cancellationToken
        ) =>
            await RunAsync(
                program,
                new ReturningDispatcher(new PluginValue.Nil()),
                cancellationToken,
                package
            );

        private static async ValueTask<PluginValue> RunAsync(
            string program,
            IPluginHostCallDispatcher dispatcher,
            CancellationToken cancellationToken,
            IReadOnlyList<PluginPackageEntry>? package = null
        )
        {
            var result = await RunResultAsync(
                program,
                dispatcher,
                PluginContractFixtures.CoroutineId(),
                cancellationToken,
                package
            );
            return result.Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>().Value;
        }

        internal static async ValueTask<PluginWorkerInvocationResult> RunResultAsync(
            string program,
            IPluginHostCallDispatcher dispatcher,
            PluginCoroutineId coroutineId,
            CancellationToken cancellationToken,
            IReadOnlyList<PluginPackageEntry>? package = null,
            PluginValue? input = null
        )
        {
            var root = Path.Combine(Path.GetTempPath(), $"blokebot-worker-test-{Guid.NewGuid():N}");
            var packageRoot = Path.Combine(root, "package");
            var stateRoot = Path.Combine(root, "state");
            _ = Directory.CreateDirectory(root);
            try
            {
                var target = CurrentTarget();
                var materializationPackage = PackageWithMain(
                    package ?? PluginContractFixtures.CompletePackage(),
                    $"return {{ run = function(input) {program} end }}\n"
                );
                var materialized = await PluginWorkerPackageMaterializer.MaterializeAsync(
                    materializationPackage,
                    target,
                    packageRoot,
                    cancellationToken
                );
                var prepared = materialized
                    .ShouldBeOfType<PluginPackageMaterializationOutcome.Prepared>()
                    .Package;
                var started = await PluginWorkerClient.StartAsync(
                    new(
                        prepared,
                        stateRoot,
                        PluginWorkerMode.Admitted,
                        dispatcher,
                        NullLogger<PluginWorkerClient>.Instance,
                        WorkerExecutable()
                    ),
                    cancellationToken
                );
                await using var client = StartedClient(started);
                var identity = Identity(prepared.Descriptor.Plugin, coroutineId);
                return await client.InvokeAsync(
                    identity,
                    new PluginLiveInvocation.Command(
                        ModuleId("main"),
                        OperationId("run"),
                        input ?? new PluginValue.Nil()
                    ),
                    cancellationToken
                );
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static PluginValue OutcomeValue(PluginHostCallOutcome outcome) =>
            outcome is PluginHostCallOutcome.Returned returned
                ? returned.Value
                : new PluginValue.Nil();

        private static PluginHostCallOutcome HostCallOutcome(
            PluginWorkerInvocationOutcome outcome
        ) =>
            outcome switch
            {
                PluginWorkerInvocationOutcome.Returned returned =>
                    new PluginHostCallOutcome.Returned(returned.Value),
                PluginWorkerInvocationOutcome.Cancelled cancelled =>
                    new PluginHostCallOutcome.Cancelled(cancelled.Reason),
                PluginWorkerInvocationOutcome.Failed => new PluginHostCallOutcome.Failed(
                    new(PluginHostFailureCode.Unavailable, "Worker invocation failed.")
                ),
                _ => throw new UnreachableException("Unknown worker invocation outcome."),
            };

        private static PluginWorkerClient StartedClient(PluginWorkerStartOutcome outcome) =>
            outcome switch
            {
                PluginWorkerStartOutcome.Started started => started.Client,
                PluginWorkerStartOutcome.Rejected rejected => throw new InvalidOperationException(
                    $"Worker handshake rejected: {rejected.Failure.Code} ({rejected.Failure.Subject})."
                ),
                PluginWorkerStartOutcome.Failed failed => throw new InvalidOperationException(
                    $"Worker start failed: {failed.Failure.Code} ({failed.Failure.SafeMessage})."
                ),
                _ => throw new UnreachableException("Unknown worker start outcome."),
            };

        private static void AssertHostCall(PluginHostCall expected, PluginHostCall actual)
        {
            actual.CoroutineId.ShouldBe(expected.CoroutineId);
            actual.Module.ShouldBe(expected.Module);
            actual.Operation.ShouldBe(expected.Operation);
            actual.Context.ShouldBe(expected.Context);
            actual.Arguments.Length.ShouldBe(expected.Arguments.Length);
            for (var index = 0; index < expected.Arguments.Length; index++)
            {
                PluginValueComparer
                    .SemanticallyEquals(expected.Arguments[index], actual.Arguments[index])
                    .ShouldBeTrue();
            }
        }
    }

    private sealed class ReturningDispatcher(PluginValue value) : IPluginHostCallDispatcher
    {
        public ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PluginHostCallOutcome>(new PluginHostCallOutcome.Returned(value));
    }

    private sealed class OutcomeDispatcher(PluginHostCallOutcome outcome)
        : IPluginHostCallDispatcher
    {
        public ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(outcome);
    }

    private sealed class CapturingDispatcher(PluginValue value) : IPluginHostCallDispatcher
    {
        internal TaskCompletionSource<PluginHostCall> Call { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        )
        {
            _ = Call.TrySetResult(call);
            return ValueTask.FromResult<PluginHostCallOutcome>(
                new PluginHostCallOutcome.Returned(value)
            );
        }
    }

    private sealed class DelayedEffectDispatcher : IPluginHostCallDispatcher
    {
        internal TaskCompletionSource<PluginHostCall> Call { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource EffectCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseLateResult { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DispatchCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ExternalEffectCompleted { get; private set; }

        public async ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        )
        {
            _ = Call.TrySetResult(call);
            ExternalEffectCompleted = true;
            _ = EffectCompleted.TrySetResult();
            await ReleaseLateResult.Task;
            _ = DispatchCompleted.TrySetResult();
            return new PluginHostCallOutcome.Returned(new PluginValue.Nil());
        }
    }

    private static IReadOnlyList<PluginPackageEntry> PackageWithMain(
        IReadOnlyList<PluginPackageEntry> package,
        string source
    ) =>
        package
            .Select(entry =>
                entry is PluginPackageEntry.File { Path: "lua/main.lua" }
                    ? new PluginPackageEntry.File("lua/main.lua", Encoding.UTF8.GetBytes(source))
                    : entry
            )
            .ToArray();

    private static PluginHostCompatibilityTarget CurrentTarget()
    {
        PluginRuntimeIdentifierResolver.TryResolveCurrent(out var runtimeIdentifier).ShouldBeTrue();
        return PluginContractFixtures.CompatibleHost() with
        {
            RuntimeIdentifier = runtimeIdentifier,
        };
    }

    private static PluginWorkerInvocationIdentity Identity(
        PluginInstallationIdentity plugin,
        PluginCoroutineId coroutineId
    )
    {
        PluginFeatureId.TryCreate("collect-links", out var feature).ShouldBeTrue();
        PluginHostId.TryCreate(1, out var host).ShouldBeTrue();
        PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var invocationId).ShouldBeTrue();
        PluginWorkerCancellationId.TryCreate(Guid.NewGuid(), out var cancellationId).ShouldBeTrue();
        PluginWorkerGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        return new(
            plugin,
            feature,
            host,
            new PluginInvocationContext.Channel(plugin, host),
            invocationId,
            coroutineId,
            generation,
            PluginWorkerDeadline.From(DateTimeOffset.UtcNow.AddSeconds(10)),
            cancellationId
        );
    }

    private static PluginWorkerExecutable WorkerExecutable() =>
        new(Path.Combine(AppContext.BaseDirectory, "plugin-worker", "BlokeBot.PluginWorker.dll"));

    private static PluginLuaModuleId ModuleId(string value) =>
        PluginLuaModuleId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid module ID '{value}'.");

    private static PluginHostOperationId OperationId(string value) =>
        PluginHostOperationId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid operation ID '{value}'.");
}
