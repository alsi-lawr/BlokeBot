using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed class EfPluginMarketplaceReceiptStore(
    IDbContextFactory<BlokeBotDbContext> contextFactory
) : IPluginMarketplaceReceiptStore, IPluginRemovalDataOwner
{
    public async ValueTask<PluginMarketplaceReceipt?> LoadAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context
            .PluginMarketplaceReceipts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.PluginId == pluginId.Value, cancellationToken);
        return record is null ? null : Map(record);
    }

    public async ValueTask SaveAsync(
        PluginMarketplaceReceipt receipt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(receipt);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.PluginMarketplaceReceipts.SingleOrDefaultAsync(
            value => value.PluginId == receipt.PluginId.Value,
            cancellationToken
        );
        if (record is null)
        {
            record = new PluginMarketplaceReceiptRecord { PluginId = receipt.PluginId.Value };
            _ = context.PluginMarketplaceReceipts.Add(record);
        }

        record.Operation = receipt.Operation;
        record.DeclaredVersion = receipt.Release?.DeclaredVersion.Value;
        record.MutableTag = receipt.Release?.Tag.Value;
        record.OutcomeCode = receipt.OutcomeCode;
        record.SafeDetail = receipt.SafeDetail;
        record.CompletedAtUtc = receipt.CompletedAt.UtcDateTime;
        _ = await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
        PluginRemovalContext context,
        CancellationToken cancellationToken
    )
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        _ = await database
            .PluginMarketplaceReceipts.Where(value => value.PluginId == context.PluginId.Value)
            .ExecuteDeleteAsync(cancellationToken);
        return new PluginLifecycleOwnerOutcome.Succeeded();
    }

    private static PluginMarketplaceReceipt Map(PluginMarketplaceReceiptRecord record)
    {
        PluginReleaseIdentity? release = null;
        if (record.DeclaredVersion is not null || record.MutableTag is not null)
        {
            if (
                !SemanticVersion.TryCreate(record.DeclaredVersion, out var version)
                || !PluginGitTag.TryCreate(record.MutableTag, out var tag)
            )
            {
                throw new InvalidOperationException(
                    "Plugin marketplace receipt contains an invalid release identity."
                );
            }

            release = new(version, tag);
        }

        var pluginId = PluginId.TryCreate(record.PluginId, out var parsedPluginId)
            ? parsedPluginId
            : throw new InvalidOperationException(
                "Plugin marketplace receipt contains an invalid plugin identifier."
            );

        return new(
            pluginId,
            record.Operation,
            release,
            record.OutcomeCode,
            record.SafeDetail,
            new DateTimeOffset(DateTime.SpecifyKind(record.CompletedAtUtc, DateTimeKind.Utc))
        );
    }
}
