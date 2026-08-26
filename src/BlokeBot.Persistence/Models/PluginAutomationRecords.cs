namespace BlokeBot.Persistence.Models;

public enum PluginAutomationInstantiationStatus
{
    InProgress,
    Completed,
    Rejected,
}

public sealed class PluginAutomationInstantiationRecord
{
    public Guid Id { get; set; }
    public Guid EnableOperationId { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string FeatureId { get; set; } = string.Empty;
    public int HostId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public string MutableTag { get; set; } = string.Empty;
    public int ManifestVersion { get; set; }
    public string TemplateHash { get; set; } = string.Empty;
    public PluginAutomationInstantiationStatus Status { get; set; }
    public Guid? FlowId { get; set; }
    public string? Diagnostic { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public AutomationFlow? Flow { get; set; }
}
