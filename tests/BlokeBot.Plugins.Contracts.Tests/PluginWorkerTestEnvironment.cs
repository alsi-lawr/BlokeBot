using System.Diagnostics;
using System.Text;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

internal sealed class MaterializedPluginTestPackage : IAsyncDisposable
{
    private readonly string _root;

    private MaterializedPluginTestPackage(string root, PreparedPluginWorkerPackage package)
    {
        _root = root;
        Package = package;
    }

    internal PreparedPluginWorkerPackage Package { get; }

    internal static async ValueTask<MaterializedPluginTestPackage> CreateAsync(
        string mainModule,
        CancellationToken cancellationToken = default
    )
    {
        var root = Path.Combine(Path.GetTempPath(), $"blokebot-worker-boundary-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(root);
        var entries = PluginContractFixtures
            .CompletePackage()
            .Select(entry =>
                entry is PluginPackageEntry.File { Path: "lua/main.lua" }
                    ? new PluginPackageEntry.File(
                        "lua/main.lua",
                        Encoding.UTF8.GetBytes(mainModule)
                    )
                    : entry
            )
            .ToArray();
        var outcome = await PluginWorkerPackageMaterializer.MaterializeAsync(
            entries,
            CurrentTarget(),
            Path.Combine(root, "package"),
            cancellationToken
        );
        return new(
            root,
            outcome.ShouldBeOfType<PluginPackageMaterializationOutcome.Prepared>().Package
        );
    }

    internal async ValueTask<StartedPluginTestWorker> StartAsync(
        PluginWorkerMode mode,
        IPluginHostCallDispatcher? dispatcher = null,
        CancellationToken cancellationToken = default
    )
    {
        var stateRoot = Path.Combine(_root, $"state-{Guid.NewGuid():N}");
        var outcome = await PluginWorkerClient.StartAsync(
            new(
                Package,
                stateRoot,
                mode,
                dispatcher ?? new ReturningTestDispatcher(new PluginValue.Nil()),
                NullLogger<PluginWorkerClient>.Instance,
                WorkerExecutable()
            ),
            cancellationToken
        );
        var client = outcome switch
        {
            PluginWorkerStartOutcome.Started started => started.Client,
            PluginWorkerStartOutcome.Rejected rejected => throw new InvalidOperationException(
                $"Worker handshake rejected: {rejected.Failure.Code}."
            ),
            PluginWorkerStartOutcome.Failed failed => throw new InvalidOperationException(
                $"Worker start failed: {failed.Failure.Code}."
            ),
            _ => throw new UnreachableException("Unknown worker start outcome."),
        };
        return new(client, stateRoot);
    }

    internal PluginWorkerStartOptions StartOptions(
        PluginWorkerMode mode,
        string stateName,
        IPluginHostCallDispatcher? dispatcher = null
    ) =>
        new(
            Package,
            Path.Combine(_root, stateName),
            mode,
            dispatcher ?? new ReturningTestDispatcher(new PluginValue.Nil()),
            NullLogger<PluginWorkerClient>.Instance,
            WorkerExecutable()
        );

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    internal static PluginWorkerInvocationIdentity Identity(
        PluginInstallationIdentity plugin,
        TimeSpan? duration = null,
        PluginCoroutineId? coroutineId = null
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
            coroutineId ?? PluginContractFixtures.CoroutineId(),
            generation,
            PluginWorkerDeadline.From(
                DateTimeOffset.UtcNow.Add(duration ?? TimeSpan.FromSeconds(10))
            ),
            cancellationId
        );
    }

    internal static PluginLuaModuleId ModuleId(string value = "main") =>
        PluginLuaModuleId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid module ID '{value}'.");

    internal static PluginHostOperationId OperationId(string value) =>
        PluginHostOperationId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid operation ID '{value}'.");

    internal static PluginWorkerExecutable WorkerExecutable() =>
        new(Path.Combine(AppContext.BaseDirectory, "plugin-worker", "BlokeBot.PluginWorker.dll"));

    private static PluginHostCompatibilityTarget CurrentTarget()
    {
        PluginRuntimeIdentifierResolver.TryResolveCurrent(out var runtimeIdentifier).ShouldBeTrue();
        return PluginContractFixtures.CompatibleHost() with
        {
            RuntimeIdentifier = runtimeIdentifier,
        };
    }
}

internal sealed record StartedPluginTestWorker(PluginWorkerClient Client, string StateRoot)
    : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Client.DisposeAsync();
}

internal sealed class ReturningTestDispatcher(PluginValue value) : IPluginHostCallDispatcher
{
    public ValueTask<PluginHostCallOutcome> DispatchAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<PluginHostCallOutcome>(new PluginHostCallOutcome.Returned(value));
}
