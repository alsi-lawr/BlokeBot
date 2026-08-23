using System.Diagnostics;
using BlokeBot.Plugins.Contracts;
using KeraLua;

namespace BlokeBot.PluginWorker;

internal sealed partial class KeraLuaPluginEngine
{
    private int? GetOrLoadModule(PluginLuaModuleId moduleId)
    {
        if (_moduleReferences.TryGetValue(moduleId, out var cached))
        {
            return cached;
        }

        var module = _package.LuaModules.FirstOrDefault(candidate => candidate.Id == moduleId);
        if (module is null)
        {
            return null;
        }

        var path = ResolveModulePath(module.Path);
        if (
            path is null
            || _lua.LoadFile(path) != LuaStatus.OK
            || _lua.PCall(0, 1, 0) != LuaStatus.OK
        )
        {
            _lua.SetTop(0);
            return null;
        }

        var reference = _lua.Ref(LuaRegistry.Index);
        _moduleReferences.Add(moduleId, reference);
        return reference;
    }

    private string? ResolveModulePath(string canonicalPath)
    {
        if (
            string.IsNullOrWhiteSpace(canonicalPath)
            || Path.IsPathRooted(canonicalPath)
            || canonicalPath.Contains('\\', StringComparison.Ordinal)
        )
        {
            return null;
        }

        var path = Path.GetFullPath(
            Path.Combine(_packageRoot, canonicalPath.Replace('/', Path.DirectorySeparatorChar))
        );
        var prefix = _packageRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _packageRoot
            : $"{_packageRoot}{Path.DirectorySeparatorChar}";
        return path.StartsWith(prefix, StringComparison.Ordinal) && File.Exists(path) ? path : null;
    }

    private void ConfigurePackagePath()
    {
        var root = _packageRoot.Replace('\\', '/');
        _lua.PushString($"{root}/?.lua;{root}/?/init.lua;{root}/lua/?.lua;{root}/lua/?/init.lua");
        _lua.SetGlobal("blokebot_package_path");
        EnsureLua(
            _lua.LoadString("package.path = blokebot_package_path .. ';' .. package.path"),
            "Lua package path could not be loaded."
        );
        EnsureLua(_lua.PCall(0, 0, 0), "Lua package path could not be initialized.");
    }

    private void OnCancellationHook(IntPtr luaState, IntPtr debug)
    {
        if (
            _executingInvocationId is not { } invocationId
            || !_cancellations.TryGetReason(invocationId, out _)
        )
        {
            return;
        }

        var state = Lua.FromIntPtr(luaState);
        state.PushString(_cancellationMarker);
        _ = state.Error();
    }

    private void CompleteExecution()
    {
        if (_execution is null)
        {
            return;
        }

        _lua.Unref(LuaRegistry.Index, _execution.ThreadReference);
        _execution.Thread.Dispose();
        _execution = null;
    }

    private static PluginEngineStep.Completed Failed(
        PluginWorkerFailureCode code,
        string message
    ) =>
        new(
            new PluginWorkerInvocationOutcome.Failed(new(code, message)),
            PluginWorkerInvocationMetrics.Empty
        );

    private static void EnsureLua(LuaStatus status, string message)
    {
        if (status != LuaStatus.OK)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class PluginInvocationExecution(
        PluginWorkerInvocationIdentity identity,
        Lua thread,
        int threadReference
    )
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        internal PluginWorkerInvocationIdentity Identity { get; } = identity;
        internal Lua Thread { get; } = thread;
        internal int ThreadReference { get; } = threadReference;
        internal PluginHostCall? PendingHostCall { get; set; }
        internal PluginWorkerFailure? LastFailure { get; set; }
        internal int ResumeCount { get; set; }
        internal int HostCallCount { get; set; }

        internal PluginWorkerInvocationMetrics Metrics() =>
            new(
                ResumeCount,
                HostCallCount,
                DiagnosticCount: 0,
                OutputBytes: 0,
                _stopwatch.ElapsedMilliseconds
            );
    }
}
