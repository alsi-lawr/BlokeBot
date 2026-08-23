using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Persistence.Models;

public sealed class PluginLifecycleOutcomeRecord
{
    public string PluginId { get; set; } = string.Empty;

    public PluginLifecycleOutcomeCode OutcomeCode { get; set; }

    public PluginLifecycleFailureCode? FailureCode { get; set; }

    public string? OutcomeDetail { get; set; }

    public DateTime OutcomeOccurredAtUtc { get; set; }
}
