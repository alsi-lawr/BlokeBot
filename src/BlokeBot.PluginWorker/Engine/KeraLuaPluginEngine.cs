using System.Text;
using BlokeBot.Plugins.Contracts;
using KeraLua;

namespace BlokeBot.PluginWorker;

internal sealed partial class KeraLuaPluginEngine : IDisposable
{
    private const string _cancellationMarker = "__BLOKEBOT_CANCELLED__";
    private const string _hostCallMarker = "blokebot-host-call-v1";
    private const string _bootstrap =
        "blokebot = blokebot or {}; blokebot.host = blokebot.host or {}; "
        + "function blokebot.host.call(module, operation, ...) "
        + "local response = coroutine.yield({ marker = '"
        + _hostCallMarker
        + "', module = module, operation = operation, arguments = {...} }); "
        + "if response.kind == 'returned' then return response.value end; "
        + "if response.kind == 'cancelled' then error('"
        + _cancellationMarker
        + "', 0) end; "
        + "error(response.safeMessage or 'Host call failed.', 0); end";

    private readonly PluginWorkerPackageDescriptor _package;
    private readonly string _packageRoot;
    private readonly PluginInvocationCancellationRegistry _cancellations;
    private readonly Lua _lua;
    private readonly LuaHookFunction _cancellationHook;
    private readonly Dictionary<PluginLuaModuleId, int> _moduleReferences = [];
    private PluginInvocationExecution? _execution;
    private PluginWorkerInvocationId? _executingInvocationId;

    internal KeraLuaPluginEngine(
        PluginWorkerPackageDescriptor package,
        string packageRoot,
        PluginInvocationCancellationRegistry cancellations
    )
    {
        _package = package;
        _packageRoot = Path.GetFullPath(packageRoot);
        _cancellations = cancellations;
        _lua = new Lua { Encoding = Encoding.UTF8 };
        _cancellationHook = OnCancellationHook;
        ConfigurePackagePath();
        EnsureLua(_lua.LoadString(_bootstrap), "Lua host bridge could not be loaded.");
        EnsureLua(_lua.PCall(0, 0, 0), "Lua host bridge could not be initialized.");
    }

    internal static PluginEngineDescriptor Descriptor => PluginWorkerEngineContract.Selected;

    internal PluginEngineStep Start(
        PluginWorkerInvocationIdentity identity,
        PluginLuaModuleId module,
        PluginHostOperationId operation,
        PluginValue input
    )
    {
        if (_execution is not null)
        {
            return Failed(
                PluginWorkerFailureCode.InvocationLimitExceeded,
                "The worker invocation limit is reached."
            );
        }

        if (_cancellations.TryGetReason(identity.InvocationId, out var cancellationReason))
        {
            return new PluginEngineStep.Cancelled(
                cancellationReason,
                PluginWorkerInvocationMetrics.Empty
            );
        }

        if (PluginValueValidator.Validate(input) is PluginValueValidationOutcome.Invalid)
        {
            return Failed(
                PluginWorkerFailureCode.InvalidValue,
                "Invocation input is outside the plugin value bounds."
            );
        }

        var moduleReference = GetOrLoadModule(module);
        if (moduleReference is null)
        {
            return Failed(PluginWorkerFailureCode.UnknownModule, "Unknown plugin Lua module.");
        }

        var thread = _lua.NewThread();
        var threadReference = _lua.Ref(LuaRegistry.Index);
        _ = _lua.RawGetInteger(LuaRegistry.Index, moduleReference.Value);
        if (!_lua.IsTable(-1))
        {
            _lua.Pop(1);
            _lua.Unref(LuaRegistry.Index, threadReference);
            return Failed(
                PluginWorkerFailureCode.EngineFailure,
                "Lua module did not return a table."
            );
        }

        _ = _lua.GetField(-1, operation.Value);
        _lua.Remove(-2);
        if (!_lua.IsFunction(-1))
        {
            _lua.Pop(1);
            _lua.Unref(LuaRegistry.Index, threadReference);
            return Failed(
                PluginWorkerFailureCode.UnknownOperation,
                "Unknown plugin Lua operation."
            );
        }

        _lua.XMove(thread, 1);
        PluginLuaValueCodec.Push(thread, input);
        thread.SetHook(_cancellationHook, LuaHookMask.Count, 10_000);
        _execution = new(identity, thread, threadReference);
        return Resume(arguments: 1, countResume: false);
    }

    internal PluginEngineStep Resume(PluginHostCallCompletion completion)
    {
        if (!MatchesHostCallCompletion(completion))
        {
            return Failed(
                PluginWorkerFailureCode.ProtocolViolation,
                "Host completion does not match the suspended coroutine."
            );
        }

        var execution = _execution!;
        execution.PendingHostCall = null;
        execution.Thread.SetTop(0);
        PushHostOutcome(execution.Thread, completion.Outcome);
        return Resume(arguments: 1, countResume: true);
    }

    internal bool MatchesHostCallCompletion(PluginHostCallCompletion completion) =>
        _execution?.PendingHostCall is { } pending
        && pending.CallId == completion.CallId
        && pending.CoroutineId == completion.CoroutineId;

    internal PluginEngineStep Cancel(PluginCancellationReason reason)
    {
        if (_execution is null)
        {
            return new PluginEngineStep.Cancelled(reason, PluginWorkerInvocationMetrics.Empty);
        }

        if (_execution.PendingHostCall is not null)
        {
            _execution.PendingHostCall = null;
            _execution.Thread.SetTop(0);
            PushHostOutcome(_execution.Thread, new PluginHostCallOutcome.Cancelled(reason));
            var resumed = Resume(arguments: 1, countResume: true);
            if (resumed is PluginEngineStep.Cancelled)
            {
                return resumed;
            }
        }

        var metrics = _execution?.Metrics() ?? PluginWorkerInvocationMetrics.Empty;
        CompleteExecution();
        return new PluginEngineStep.Cancelled(reason, metrics);
    }

    public void Dispose()
    {
        CompleteExecution();
        foreach (var reference in _moduleReferences.Values)
        {
            _lua.Unref(LuaRegistry.Index, reference);
        }

        _lua.Dispose();
    }

    private PluginEngineStep Resume(int arguments, bool countResume)
    {
        var execution = _execution!;
        if (countResume)
        {
            execution.ResumeCount++;
        }

        _executingInvocationId = execution.Identity.InvocationId;
        LuaStatus status;
        int resultCount;
        try
        {
            status = execution.Thread.Resume(null, arguments, out resultCount);
        }
        finally
        {
            _executingInvocationId = null;
        }

        if (
            _cancellations.TryGetReason(execution.Identity.InvocationId, out var cancellationReason)
        )
        {
            var metrics = execution.Metrics();
            CompleteExecution();
            return new PluginEngineStep.Cancelled(cancellationReason, metrics);
        }

        if (status == LuaStatus.Yield)
        {
            var call = ReadHostCall(execution, resultCount);
            if (call is null)
            {
                var failure = execution.LastFailure!;
                var metrics = execution.Metrics();
                CompleteExecution();
                return new PluginEngineStep.Completed(
                    new PluginWorkerInvocationOutcome.Failed(failure),
                    metrics
                );
            }

            execution.PendingHostCall = call;
            execution.HostCallCount++;
            execution.Thread.SetTop(0);
            return new PluginEngineStep.HostCall(call);
        }

        if (status != LuaStatus.OK)
        {
            var metrics = execution.Metrics();
            CompleteExecution();
            return new PluginEngineStep.Completed(
                new PluginWorkerInvocationOutcome.Failed(
                    new(PluginWorkerFailureCode.EngineFailure, "Lua execution failed.")
                ),
                metrics
            );
        }

        var outcome = ReadResult(execution, resultCount);
        var completedMetrics = execution.Metrics();
        CompleteExecution();
        return new PluginEngineStep.Completed(outcome, completedMetrics);
    }

    private static PluginWorkerInvocationOutcome ReadResult(
        PluginInvocationExecution execution,
        int resultCount
    ) =>
        resultCount switch
        {
            0 => new PluginWorkerInvocationOutcome.Returned(new PluginValue.Nil()),
            1 => PluginLuaValueCodec
                .Read(execution.Thread, -1)
                .Match<PluginWorkerInvocationOutcome>(
                    mapped => new PluginWorkerInvocationOutcome.Returned(mapped.Value),
                    rejected => new PluginWorkerInvocationOutcome.Failed(rejected.Failure)
                ),
            _ => new PluginWorkerInvocationOutcome.Failed(
                new(
                    PluginWorkerFailureCode.OutputLimitExceeded,
                    "Lua returned more than one result."
                )
            ),
        };
}
