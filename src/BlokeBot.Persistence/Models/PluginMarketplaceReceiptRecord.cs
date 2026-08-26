using BlokeBot.Plugins.Features;

namespace BlokeBot.Persistence.Models;

public sealed class PluginMarketplaceReceiptRecord
{
    public string PluginId { get; set; } = string.Empty;

    public PluginMarketplaceOperationKind Operation { get; set; }

    public string? DeclaredVersion { get; set; }

    public string? MutableTag { get; set; }

    public string OutcomeCode { get; set; } = string.Empty;

    public string? SafeDetail { get; set; }

    public DateTime CompletedAtUtc { get; set; }
}
