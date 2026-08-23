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
}
