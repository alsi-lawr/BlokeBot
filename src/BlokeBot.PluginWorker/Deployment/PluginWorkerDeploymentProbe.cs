using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.PluginWorker;

internal static class PluginWorkerDeploymentProbe
{
    internal static Task<int> RunAsync(string stateRoot)
    {
        var originalDirectory = Environment.CurrentDirectory;
        var root = Path.Combine(Path.GetFullPath(stateRoot), $"probe-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(root, "package");
        var writableRoot = Path.Combine(root, "state");
        try
        {
            _ = Directory.CreateDirectory(Path.Combine(packageRoot, "lua"));
            _ = Directory.CreateDirectory(writableRoot);
            File.WriteAllText(
                Path.Combine(packageRoot, "lua", "probe_module.lua"),
                "return { answer = 42 }"
            );
            File.WriteAllText(
                Path.Combine(packageRoot, "lua", "main.lua"),
                """
                local required = require("probe_module")
                return { run = function()
                  assert(_VERSION == "Lua 5.4")
                  assert(type(coroutine) == "table" and type(debug) == "table")
                  assert(type(io) == "table" and type(math) == "table")
                  assert(type(os) == "table" and type(package) == "table")
                  assert(type(string) == "table" and type(table) == "table")
                  assert(type(utf8) == "table")
                  local thread = coroutine.create(function() coroutine.yield(21); return 42 end)
                  local ok, yielded = coroutine.resume(thread)
                  assert(ok and yielded == 21)
                  local resumed, answer = coroutine.resume(thread)
                  assert(resumed and answer == required.answer)
                  local file = assert(io.open("worker-probe-state", "w"))
                  file:write("writable")
                  file:close()
                  return { engine = _VERSION, answer = required.answer }
                end }
                """
            );
            Directory.SetCurrentDirectory(writableRoot);
            using var cancellations = new PluginInvocationCancellationRegistry();
            using var engine = new KeraLuaPluginEngine(Package(), packageRoot, cancellations);
            var identity = Identity();
            _ = cancellations.Begin(identity);
            var result = engine.Start(
                identity,
                ModuleId("main"),
                OperationId("run"),
                new PluginValue.Nil()
            );
            cancellations.Complete(identity.InvocationId);
            return Task.FromResult(Passed(result) ? 0 : 1);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static bool Passed(PluginEngineStep result) =>
        result
            is PluginEngineStep.Completed
            {
                Outcome: PluginWorkerInvocationOutcome.Returned { Value: PluginValue.Map value },
            }
        && value.Properties.Any(property =>
            property is { Name: "engine", Value: PluginValue.String { Value: "Lua 5.4" } }
        )
        && value.Properties.Any(property =>
            property is { Name: "answer", Value: PluginValue.Number { Value: 42 } }
        );

    private static PluginWorkerPackageDescriptor Package() =>
        new(
            new(Plugin(), new(Version(), Tag())),
            RuntimeIdentifier(),
            ModuleId("main"),
            [
                new PluginWorkerLuaModule(ModuleId("main"), "lua/main.lua"),
                new PluginWorkerLuaModule(ModuleId("probe-module"), "lua/probe_module.lua"),
            ]
        );

    private static PluginWorkerInvocationIdentity Identity()
    {
        var package = Package();
        return new(
            package.Plugin,
            Feature(),
            Host(),
            new PluginInvocationContext.Channel(package.Plugin, Host()),
            Invocation(),
            Coroutine(),
            Generation(),
            PluginWorkerDeadline.From(DateTimeOffset.UtcNow.AddSeconds(10)),
            Cancellation()
        );
    }

    private static PluginId Plugin() =>
        PluginId.TryCreate("blokebot.deployment-probe", out var id)
            ? id
            : throw new InvalidOperationException("Invalid deployment-probe plugin ID.");

    private static SemanticVersion Version() =>
        SemanticVersion.TryCreate("1.0.0", out var version)
            ? version
            : throw new InvalidOperationException("Invalid deployment-probe version.");

    private static PluginGitTag Tag() =>
        PluginGitTag.TryCreate("deployment-probe", out var tag)
            ? tag
            : throw new InvalidOperationException("Invalid deployment-probe tag.");

    private static PluginRuntimeIdentifier RuntimeIdentifier() =>
        PluginRuntimeIdentifierResolver.TryResolveCurrent(out var runtimeIdentifier)
            ? runtimeIdentifier
            : throw new InvalidOperationException("Unsupported deployment-probe runtime.");

    private static PluginFeatureId Feature() =>
        PluginFeatureId.TryCreate("probe", out var feature)
            ? feature
            : throw new InvalidOperationException("Invalid deployment-probe feature ID.");

    private static PluginHostId Host() =>
        PluginHostId.TryCreate(1, out var host)
            ? host
            : throw new InvalidOperationException("Invalid deployment-probe host ID.");

    private static PluginWorkerInvocationId Invocation() =>
        PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var invocation)
            ? invocation
            : throw new InvalidOperationException("Invalid deployment-probe invocation ID.");

    private static PluginCoroutineId Coroutine() =>
        PluginCoroutineId.TryCreate(Guid.NewGuid(), out var coroutine)
            ? coroutine
            : throw new InvalidOperationException("Invalid deployment-probe coroutine ID.");

    private static PluginWorkerGeneration Generation() =>
        PluginWorkerGeneration.TryCreate(1, out var generation)
            ? generation
            : throw new InvalidOperationException("Invalid deployment-probe generation.");

    private static PluginWorkerCancellationId Cancellation() =>
        PluginWorkerCancellationId.TryCreate(Guid.NewGuid(), out var cancellation)
            ? cancellation
            : throw new InvalidOperationException("Invalid deployment-probe cancellation ID.");

    private static PluginLuaModuleId ModuleId(string value) =>
        PluginLuaModuleId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid deployment-probe module ID.");

    private static PluginHostOperationId OperationId(string value) =>
        PluginHostOperationId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid deployment-probe operation ID.");
}
