namespace BlokeBot.Persistence.Models;

public enum AutomationFlowRunStatus
{
    Running,
    Waiting,
    Completed,
    Failed,
    Invalidated,
}

public enum AutomationNodeRunStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    ContinuedAfterFailure,
    Invalidated,
}

public enum PersistedAutomationEdgeKind
{
    Flow,
    Data,
}

public sealed class AutomationFlow
{
    public Guid Id { get; set; }
    public int HostId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public bool IsEnabled { get; set; }
    public bool UseVerticalLayout { get; set; }
    public bool UseSmoothEdges { get; set; }
    public string? UnavailableReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<AutomationFlowNode> Nodes { get; set; } = [];
    public List<AutomationFlowEdge> Edges { get; set; } = [];
}

public sealed class AutomationFlowNode
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int DefinitionSchemaVersion { get; set; }
    public string ConfigurationJson { get; set; } = string.Empty;
    public string InputBindingsJson { get; set; } = string.Empty;
    public int ExpressionLanguageVersion { get; set; }
    public bool ContinueOnFailure { get; set; }
    public int CanvasX { get; set; }
    public int CanvasY { get; set; }
    public string? DisplayAlias { get; set; }
    public string? PluginProvenanceJson { get; set; }
    public AutomationFlow Flow { get; set; } = null!;
}

public sealed class AutomationFlowEdge
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public PersistedAutomationEdgeKind Kind { get; set; }
    public Guid SourceNodeId { get; set; }
    public string SourcePortId { get; set; } = string.Empty;
    public Guid TargetNodeId { get; set; }
    public string TargetPortId { get; set; } = string.Empty;
    public AutomationFlow Flow { get; set; } = null!;
}

public sealed class AutomationFlowRun
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public int HostId { get; set; }
    public int AutomationGeneration { get; set; }
    public HostFeatureFlags RequiredFeatures { get; set; }
    public int ContextSchemaVersion { get; set; }
    public string SourceDefinitionId { get; set; } = string.Empty;
    public Guid SourceNodeId { get; set; }
    public Guid SourceOccurrenceId { get; set; }
    public string ContextJson { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
    public AutomationFlowRunStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? ExecutionLeaseId { get; set; }
    public AutomationFlow Flow { get; set; } = null!;
    public List<AutomationNodeRun> NodeRuns { get; set; } = [];
}

/// <summary>
/// A short-lived Twitch EventSub delivery receipt. A row has deduplication authority for exactly
/// ten minutes from <see cref="ClaimedAtUtc"/>; at <see cref="ExpiresAtUtc"/> it is dead and only
/// awaits physical cleanup. Nothing beyond this bounded window is retained.
/// </summary>
public sealed class AutomationEventReceipt
{
    public int HostId { get; set; }
    public string SourceDefinitionId { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
    public DateTime ClaimedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class AutomationNodeRun
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public Guid NodeId { get; set; }
    public long Sequence { get; set; }
    public AutomationNodeRunStatus Status { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? OutcomeCode { get; set; }
    public string? OutputJson { get; set; }
    public AutomationFlowRun Run { get; set; } = null!;
}
