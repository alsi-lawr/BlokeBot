using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginWorkerInvocationIdentity(
    PluginInstallationIdentity Plugin,
    PluginFeatureId Feature,
    PluginHostId Host,
    PluginInvocationContext Context,
    PluginWorkerInvocationId InvocationId,
    PluginCoroutineId CoroutineId,
    PluginWorkerGeneration Generation,
    PluginWorkerDeadline Deadline,
    PluginWorkerCancellationId CancellationId,
    PluginActivationFence? Activation = null
);

public sealed record PluginPreparationInvocation(
    PluginLuaModuleId Module,
    PluginHostOperationId Operation,
    PluginValue Input
);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "category")]
[JsonDerivedType(typeof(PluginLiveInvocation.Migration), "migration")]
[JsonDerivedType(typeof(PluginLiveInvocation.Lifecycle), "lifecycle")]
[JsonDerivedType(typeof(PluginLiveInvocation.Command), "command")]
[JsonDerivedType(typeof(PluginLiveInvocation.Event), "event")]
[JsonDerivedType(typeof(PluginLiveInvocation.Schedule), "schedule")]
[JsonDerivedType(typeof(PluginLiveInvocation.HostAction), "host-action")]
[JsonDerivedType(typeof(PluginLiveInvocation.Storage), "storage")]
[JsonDerivedType(typeof(PluginLiveInvocation.Page), "page")]
[JsonDerivedType(typeof(PluginLiveInvocation.Automation), "automation")]
public abstract record PluginLiveInvocation
{
    private PluginLiveInvocation(
        PluginLuaModuleId module,
        PluginHostOperationId operation,
        PluginValue input
    )
    {
        Module = module;
        Operation = operation;
        Input = input;
    }

    public PluginLuaModuleId Module { get; }

    public PluginHostOperationId Operation { get; }

    public PluginValue Input { get; }

    public sealed record Migration(
        PluginLuaModuleId Module,
        PluginHostOperationId Operation,
        PluginValue Input
    ) : PluginLiveInvocation(Module, Operation, Input);

    public sealed record Lifecycle(
        PluginLuaModuleId Module,
        PluginHostOperationId Operation,
        PluginValue Input
    ) : PluginLiveInvocation(Module, Operation, Input);

    public sealed record Command(
        PluginLuaModuleId Module,
        PluginHostOperationId Operation,
        PluginValue Input
    ) : PluginLiveInvocation(Module, Operation, Input);

    public sealed record Event(
        PluginLuaModuleId Module,
        PluginHostOperationId Operation,
        PluginValue Input
    ) : PluginLiveInvocation(Module, Operation, Input);

    public sealed record Schedule(
        PluginLuaModuleId Module,
        PluginHostOperationId Operation,
        PluginValue Input
    ) : PluginLiveInvocation(Module, Operation, Input);

    public sealed record HostAction(
        PluginLuaModuleId Module,
        PluginHostOperationId Operation,
        PluginValue Input
    ) : PluginLiveInvocation(Module, Operation, Input);

    public sealed record Storage(
        PluginLuaModuleId Module,
        PluginHostOperationId Operation,
        PluginValue Input
    ) : PluginLiveInvocation(Module, Operation, Input);

    public sealed record Page(
        PluginLuaModuleId Module,
        PluginHostOperationId Operation,
        PluginValue Input
    ) : PluginLiveInvocation(Module, Operation, Input);

    public sealed record Automation(
        PluginLuaModuleId Module,
        PluginHostOperationId Operation,
        PluginAutomationDefinitionId Definition,
        PluginAutomationDefinitionKind Kind,
        PluginValue Input
    ) : PluginLiveInvocation(Module, Operation, Input);
}
