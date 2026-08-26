using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed partial class EfPluginFeatureStore
{
    public async ValueTask<PluginFeatureEnableStoreOutcome> EnableAsync(
        PluginFeatureEnableStoreRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            var key = request.NextState.Key;
            if (
                await InstallationRevisionAsync(db, key.PluginId, cancellationToken)
                != request.ExpectedInstallationRevision.Value
            )
            {
                return new PluginFeatureEnableStoreOutcome.Conflict(
                    PluginFeatureEnableConflictCode.InstallationConfiguration,
                    await LoadFeatureStateAsync(key, cancellationToken)
                );
            }
            if (
                await FeatureRevisionAsync(db, key, cancellationToken)
                != request.ExpectedFeatureRevision.Value
            )
            {
                return new PluginFeatureEnableStoreOutcome.Conflict(
                    PluginFeatureEnableConflictCode.FeatureConfiguration,
                    await LoadFeatureStateAsync(key, cancellationToken)
                );
            }

            var record = await FindStateAsync(db, key, cancellationToken);
            if (!Expected(record, request.ExpectedState))
            {
                return new PluginFeatureEnableStoreOutcome.Conflict(
                    PluginFeatureEnableConflictCode.FeatureState,
                    record is null ? null : PluginFeatureRecordMapper.ToDomain(record)
                );
            }
            if (
                await ApplyAutomationsAsync(db, request, cancellationToken) is
                { } automationConflict
            )
            {
                _ = await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new PluginFeatureEnableStoreOutcome.Conflict(
                    automationConflict,
                    record is null ? null : PluginFeatureRecordMapper.ToDomain(record)
                );
            }
            if (record is null)
            {
                record = PluginFeatureRecordMapper.ToRecord(request.NextState);
                _ = db.PluginFeatureStates.Add(record);
            }
            else
            {
                PluginFeatureRecordMapper.Apply(record, request.NextState);
            }

            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PluginFeatureEnableStoreOutcome.Enabled(
                PluginFeatureRecordMapper.ToDomain(record)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return new PluginFeatureEnableStoreOutcome.Conflict(
                PluginFeatureEnableConflictCode.FeatureState,
                await LoadFeatureStateAsync(request.NextState.Key, cancellationToken)
            );
        }
        catch (DbUpdateException exception) when (UniqueConstraint(exception))
        {
            return new PluginFeatureEnableStoreOutcome.Conflict(
                PluginFeatureEnableConflictCode.FeatureState,
                await LoadFeatureStateAsync(request.NextState.Key, cancellationToken)
            );
        }
    }

    public async ValueTask<PluginFeatureStateStoreWriteOutcome> WriteFeatureStateAsync(
        PluginFeatureState expected,
        PluginFeatureState next,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var record = await FindStateAsync(db, expected.Key, cancellationToken);
            if (!Expected(record, expected))
            {
                return new PluginFeatureStateStoreWriteOutcome.Conflict(
                    record is null ? null : PluginFeatureRecordMapper.ToDomain(record)
                );
            }
            PluginFeatureRecordMapper.Apply(record!, next);
            _ = await db.SaveChangesAsync(cancellationToken);
            return new PluginFeatureStateStoreWriteOutcome.Written(
                PluginFeatureRecordMapper.ToDomain(record!)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return new PluginFeatureStateStoreWriteOutcome.Conflict(
                await LoadFeatureStateAsync(expected.Key, cancellationToken)
            );
        }
    }

    private static async Task<long> InstallationRevisionAsync(
        BlokeBotDbContext db,
        PluginId pluginId,
        CancellationToken cancellationToken
    ) =>
        await db
            .PluginInstallationConfigurations.Where(value => value.PluginId == pluginId.Value)
            .Select(value => (long?)value.Revision)
            .SingleOrDefaultAsync(cancellationToken)
        ?? 0;

    private static async Task<long> FeatureRevisionAsync(
        BlokeBotDbContext db,
        PluginFeatureKey key,
        CancellationToken cancellationToken
    ) =>
        await db
            .PluginFeatureConfigurations.Where(value =>
                value.PluginId == key.PluginId.Value
                && value.FeatureId == key.FeatureId.Value
                && value.HostId == key.HostId.Value
            )
            .Select(value => (long?)value.Revision)
            .SingleOrDefaultAsync(cancellationToken)
        ?? 0;

    private static Task<PluginFeatureStateRecord?> FindStateAsync(
        BlokeBotDbContext db,
        PluginFeatureKey key,
        CancellationToken cancellationToken
    ) =>
        db.PluginFeatureStates.SingleOrDefaultAsync(
            value =>
                value.PluginId == key.PluginId.Value
                && value.FeatureId == key.FeatureId.Value
                && value.HostId == key.HostId.Value,
            cancellationToken
        );

    private static bool Expected(PluginFeatureStateRecord? record, PluginFeatureState? expected) =>
        (record, expected) switch
        {
            (null, null) => true,
            ({ } current, { } state) => current.Revision == state.Revision.Value,
            _ => false,
        };
}
