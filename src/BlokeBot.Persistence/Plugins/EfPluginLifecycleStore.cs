using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed class EfPluginLifecycleStore(IDbContextFactory<BlokeBotDbContext> dbFactory)
    : IPluginLifecycleStore
{
    public async ValueTask<PluginLifecycleState?> LoadAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db
            .PluginLifecycles.AsNoTracking()
            .SingleOrDefaultAsync(value => value.PluginId == pluginId.Value, cancellationToken);
        return record is null ? null : PluginLifecycleRecordMapper.ToDomain(record);
    }

    public async ValueTask<IReadOnlyList<PluginLifecycleState>> LoadAllAsync(
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var records = await db
            .PluginLifecycles.AsNoTracking()
            .OrderBy(value => value.PluginId)
            .ToArrayAsync(cancellationToken);
        return records.Select(PluginLifecycleRecordMapper.ToDomain).ToArray();
    }

    public async ValueTask<PluginLifecycleTombstone?> LoadTombstoneAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db
            .PluginLifecycleOutcomes.AsNoTracking()
            .SingleOrDefaultAsync(value => value.PluginId == pluginId.Value, cancellationToken);
        return record is null ? null : PluginLifecycleOutcomeRecordMapper.ToDomain(record);
    }

    public async ValueTask<PluginLifecycleStoreBeginOutcome> BeginActivationAsync(
        PluginLifecycleBeginRequest request,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.PluginLifecycles.SingleOrDefaultAsync(
            value => value.PluginId == request.Installation.PluginId.Value,
            cancellationToken
        );
        var tombstone = await db.PluginLifecycleOutcomes.SingleOrDefaultAsync(
            value => value.PluginId == request.Installation.PluginId.Value,
            cancellationToken
        );
        var current = record is null ? null : PluginLifecycleRecordMapper.ToDomain(record);
        var transition = PluginLifecycleStateMachine.BeginActivation(
            current,
            request.Installation,
            request.OperationId,
            request.OccurredAtUtc
        );
        if (transition is PluginLifecycleTransitionOutcome.Rejected rejected)
        {
            return new PluginLifecycleStoreBeginOutcome.Rejected(rejected.Code, current);
        }

        var begun = ((PluginLifecycleTransitionOutcome.Applied)transition).State;
        if (record is null)
        {
            _ = db.PluginLifecycles.Add(PluginLifecycleRecordMapper.FromDomain(begun));
        }
        else
        {
            PluginLifecycleRecordMapper.Apply(record, begun);
        }

        if (tombstone is not null)
        {
            _ = db.PluginLifecycleOutcomes.Remove(tombstone);
        }

        try
        {
            _ = await db.SaveChangesAsync(cancellationToken);
            return new PluginLifecycleStoreBeginOutcome.Begun(begun);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new PluginLifecycleStoreBeginOutcome.Rejected(
                PluginLifecycleTransitionFailureCode.Busy,
                await LoadAsync(request.Installation.PluginId, cancellationToken)
            );
        }
        catch (DbUpdateException)
        {
            var concurrent = await LoadAsync(request.Installation.PluginId, cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            return new PluginLifecycleStoreBeginOutcome.Rejected(
                PluginLifecycleTransitionFailureCode.Busy,
                concurrent
            );
        }
    }

    public async ValueTask<PluginLifecycleStoreWriteOutcome> WriteAsync(
        PluginLifecycleState expected,
        PluginLifecycleState next,
        CancellationToken cancellationToken
    )
    {
        if (next.PluginId != expected.PluginId || next.Revision != expected.Revision + 1)
        {
            return new PluginLifecycleStoreWriteOutcome.Conflict(
                await LoadAsync(expected.PluginId, cancellationToken)
            );
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.PluginLifecycles.SingleOrDefaultAsync(
            value => value.PluginId == expected.PluginId.Value,
            cancellationToken
        );
        if (record is null || PluginLifecycleRecordMapper.ToDomain(record) != expected)
        {
            return new PluginLifecycleStoreWriteOutcome.Conflict(
                record is null ? null : PluginLifecycleRecordMapper.ToDomain(record)
            );
        }

        PluginLifecycleRecordMapper.Apply(record, next);
        try
        {
            _ = await db.SaveChangesAsync(cancellationToken);
            return new PluginLifecycleStoreWriteOutcome.Written(next);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new PluginLifecycleStoreWriteOutcome.Conflict(
                await LoadAsync(expected.PluginId, cancellationToken)
            );
        }
    }

    public async ValueTask<PluginLifecycleStorePurgeOutcome> CompletePurgeAsync(
        PluginLifecycleState expected,
        PluginLifecycleOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        if (
            expected.Phase != PluginLifecyclePhase.Purging
            || outcome is not { Code: PluginLifecycleOutcomeCode.Purged, FailureCode: null }
        )
        {
            return new PluginLifecycleStorePurgeOutcome.Conflict(
                await LoadAsync(expected.PluginId, cancellationToken)
            );
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.PluginLifecycles.SingleOrDefaultAsync(
            value => value.PluginId == expected.PluginId.Value,
            cancellationToken
        );
        var retained = await db.PluginLifecycleOutcomes.SingleOrDefaultAsync(
            value => value.PluginId == expected.PluginId.Value,
            cancellationToken
        );
        if (record is null)
        {
            return retained is null
                ? new PluginLifecycleStorePurgeOutcome.Conflict(null)
                : new PluginLifecycleStorePurgeOutcome.Completed(
                    PluginLifecycleOutcomeRecordMapper.ToDomain(retained)
                );
        }

        var current = PluginLifecycleRecordMapper.ToDomain(record);
        if (current != expected)
        {
            return new PluginLifecycleStorePurgeOutcome.Conflict(current);
        }

        var tombstone = new PluginLifecycleTombstone(expected.PluginId, outcome);
        _ = db.PluginLifecycles.Remove(record);
        if (retained is null)
        {
            _ = db.PluginLifecycleOutcomes.Add(
                PluginLifecycleOutcomeRecordMapper.FromDomain(tombstone)
            );
        }
        else
        {
            db.Entry(retained)
                .CurrentValues.SetValues(PluginLifecycleOutcomeRecordMapper.FromDomain(tombstone));
        }

        try
        {
            _ = await db.SaveChangesAsync(cancellationToken);
            return new PluginLifecycleStorePurgeOutcome.Completed(tombstone);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolvePurgeRaceAsync(expected.PluginId, cancellationToken);
        }
        catch (DbUpdateException)
        {
            var resolved = await ResolvePurgeRaceAsync(expected.PluginId, cancellationToken);
            if (resolved is PluginLifecycleStorePurgeOutcome.Completed)
            {
                return resolved;
            }

            throw;
        }
    }

    private async ValueTask<PluginLifecycleStorePurgeOutcome> ResolvePurgeRaceAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        var tombstone = await LoadTombstoneAsync(pluginId, cancellationToken);
        return tombstone is not null
            ? new PluginLifecycleStorePurgeOutcome.Completed(tombstone)
            : new PluginLifecycleStorePurgeOutcome.Conflict(
                await LoadAsync(pluginId, cancellationToken)
            );
    }
}
