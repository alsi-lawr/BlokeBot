using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginHostModuleRequirement(
    PluginHostModuleId Id,
    PluginApiVersion MinimumVersion,
    PluginApiVersion MaximumVersion
);

public enum PluginInvocationContextKind
{
    Installation,
    Channel,
    Automation,
    Migration,
    Page,
}

public sealed record PluginHostModuleDescriptor(
    PluginHostModuleId Id,
    PluginApiVersion Version,
    ImmutableArray<PluginHostOperationDescriptor> Operations
);

public sealed record PluginHostOperationDescriptor(
    PluginHostOperationId Id,
    ImmutableArray<PluginInvocationContextKind> PermittedContexts,
    ImmutableArray<PluginValueKind> ArgumentKinds,
    PluginValueKind ResultKind,
    bool SupportsCancellation,
    int MaximumArgumentBytes,
    int MaximumResultBytes
);
