using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public enum PluginMarketplaceOperationKind
{
    Install,
    Update,
    Remove,
    Restart,
}

public sealed record PluginMarketplaceReceipt(
    PluginId PluginId,
    PluginMarketplaceOperationKind Operation,
    PluginReleaseIdentity? Release,
    string OutcomeCode,
    string? SafeDetail,
    DateTimeOffset CompletedAt
);

public interface IPluginMarketplaceReceiptStore
{
    ValueTask<PluginMarketplaceReceipt?> LoadAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    );

    ValueTask SaveAsync(PluginMarketplaceReceipt receipt, CancellationToken cancellationToken);
}
