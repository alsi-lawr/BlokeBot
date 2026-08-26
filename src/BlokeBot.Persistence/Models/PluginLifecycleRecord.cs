using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Persistence.Models;

public sealed class PluginLifecycleRecord
{
    public string PluginId { get; set; } = string.Empty;

    public string SelectedVersion { get; set; } = string.Empty;

    public string SelectedTag { get; set; } = string.Empty;

    public Guid SelectedPackageOperationId { get; set; }

    public Guid OperationId { get; set; }

    public long SelectedGeneration { get; set; }

    public string? ActiveVersion { get; set; }

    public string? ActiveTag { get; set; }

    public Guid? ActiveOperationId { get; set; }

    public Guid? ActivePackageOperationId { get; set; }

    public long? ActiveGeneration { get; set; }

    public PluginLifecyclePhase Phase { get; set; }

    public PluginLifecycleOperationKind OperationKind { get; set; }

    public PluginLifecyclePhase? FaultedFrom { get; set; }

    public bool AutomaticRestartConsumed { get; set; }

    public DateTime? RestartNotBeforeUtc { get; set; }

    public PluginLifecycleOutcomeCode OutcomeCode { get; set; }

    public PluginLifecycleFailureCode? FailureCode { get; set; }

    public string? OutcomeDetail { get; set; }

    public DateTime OutcomeOccurredAtUtc { get; set; }

    public long Revision { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
