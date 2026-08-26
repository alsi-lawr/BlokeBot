using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public sealed record PluginLifecycleOperationId
{
    private PluginLifecycleOperationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PluginLifecycleOperationId New() => new(Guid.NewGuid());

    public static bool TryCreate(Guid candidate, out PluginLifecycleOperationId operationId)
    {
        var valid = candidate != Guid.Empty;
        operationId = valid ? new(candidate) : null!;
        return valid;
    }

    public override string ToString() => Value.ToString("D");
}

public sealed record PluginPackageOperationId
{
    private PluginPackageOperationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PluginPackageOperationId New() => new(Guid.NewGuid());

    public static PluginPackageOperationId FromLifecycleOperation(
        PluginLifecycleOperationId operationId
    ) => new(operationId.Value);

    public static bool TryCreate(Guid candidate, out PluginPackageOperationId operationId)
    {
        var valid = candidate != Guid.Empty;
        operationId = valid ? new(candidate) : null!;
        return valid;
    }

    public override string ToString() => Value.ToString("D");
}

public sealed record PluginLifecycleFence(
    PluginLifecycleOperationId OperationId,
    PluginWorkerGeneration Generation
);

internal static class PluginLifecycleGenerations
{
    internal static bool TryNext(
        PluginWorkerGeneration? current,
        out PluginWorkerGeneration generation
    )
    {
        var candidate = current is null ? 1UL : current.Value + 1UL;
        return candidate > long.MaxValue
            ? Fail(out generation)
            : PluginWorkerGeneration.TryCreate(candidate, out generation);
    }

    private static bool Fail(out PluginWorkerGeneration generation)
    {
        generation = null!;
        return false;
    }
}
