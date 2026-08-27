using BlokeBot.Plugins.Contracts;
using KeraLua;

namespace BlokeBot.PluginWorker;

internal sealed partial class KeraLuaPluginEngine
{
    private PluginHostCall? ReadHostCall(PluginInvocationExecution execution, int resultCount)
    {
        if (
            resultCount != 1
            || PluginLuaValueCodec.Read(execution.Thread, -1)
                is not PluginLuaValueReadOutcome.Mapped { Value: PluginValue.Map yielded }
        )
        {
            execution.LastFailure = new(
                PluginWorkerFailureCode.EngineFailure,
                "Lua yielded outside the typed host-call boundary."
            );
            return null;
        }

        var properties = yielded.Properties.ToDictionary(
            property => property.Name,
            property => property.Value
        );
        if (
            !properties.TryGetValue("marker", out var marker)
            || marker is not PluginValue.String { Value: _hostCallMarker }
            || !properties.TryGetValue("module", out var moduleValue)
            || moduleValue is not PluginValue.String moduleText
            || !PluginHostModuleId.TryCreate(moduleText.Value, out var module)
            || !properties.TryGetValue("operation", out var operationValue)
            || operationValue is not PluginValue.String operationText
            || !PluginHostOperationId.TryCreate(operationText.Value, out var operation)
            || !properties.TryGetValue("arguments", out var argumentsValue)
            || Arguments(argumentsValue) is not { } arguments
            || !PluginHostCallId.TryCreate(Guid.NewGuid(), out var callId)
        )
        {
            execution.LastFailure = new(
                PluginWorkerFailureCode.EngineFailure,
                "Lua yielded an invalid typed host call."
            );
            return null;
        }

        return new(
            callId,
            execution.Identity.CoroutineId,
            module,
            operation,
            execution.Identity.Context,
            arguments.Items
        );
    }

    private static PluginValue.Array? Arguments(PluginValue value) =>
        value switch
        {
            PluginValue.Array arguments => arguments,
            PluginValue.Map { Properties.IsEmpty: true } => new([]),
            _ => null,
        };

    private static void PushHostOutcome(Lua lua, PluginHostCallOutcome outcome)
    {
        lua.CreateTable(0, 2);
        var table = lua.AbsIndex(-1);
        switch (outcome)
        {
            case PluginHostCallOutcome.Returned returned:
                lua.PushString("kind");
                lua.PushString("returned");
                lua.RawSet(table);
                if (returned.Value is not PluginValue.Nil)
                {
                    lua.PushString("value");
                    PluginLuaValueCodec.Push(lua, returned.Value);
                    lua.RawSet(table);
                }

                break;
            case PluginHostCallOutcome.Failed failed:
                lua.PushString("kind");
                lua.PushString("failed");
                lua.RawSet(table);
                lua.PushString("safeMessage");
                lua.PushString(failed.Failure.SafeMessage);
                lua.RawSet(table);
                break;
            case PluginHostCallOutcome.Cancelled:
                lua.PushString("kind");
                lua.PushString("cancelled");
                lua.RawSet(table);
                break;
        }
    }
}
