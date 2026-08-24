using BlokeBot.Plugins.Features;

namespace BlokeBot.Persistence.Models;

public sealed class PluginInstallationConfigurationRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string ValuesJson { get; set; } = "[]";
    public long Revision { get; set; }
}

public sealed class PluginInstallationSecretRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string SettingId { get; set; } = string.Empty;
    public byte[] ProtectedValue { get; set; } = [];
}

public sealed class PluginFeatureConfigurationRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string FeatureId { get; set; } = string.Empty;
    public int HostId { get; set; }
    public string ValuesJson { get; set; } = "[]";
    public long Revision { get; set; }
}

public sealed class PluginFeatureSecretRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string FeatureId { get; set; } = string.Empty;
    public int HostId { get; set; }
    public string SettingId { get; set; } = string.Empty;
    public byte[] ProtectedValue { get; set; } = [];
}

public enum PluginFeatureReadinessKind
{
    Disabled,
    EnabledDegraded,
    Ready,
}

public sealed class PluginFeatureStateRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string FeatureId { get; set; } = string.Empty;
    public int HostId { get; set; }
    public Guid LifecycleOperationId { get; set; }
    public long WorkerGeneration { get; set; }
    public long FeatureGeneration { get; set; }
    public PluginFeatureReadinessKind Readiness { get; set; }
    public PluginReadinessReasonCode? ReasonCode { get; set; }
    public PluginRecoveryAction? RecoveryAction { get; set; }
    public string? ReasonDetail { get; set; }
    public long Revision { get; set; }
}
