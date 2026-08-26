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
    ) => await BeginAsync(request, replace: false, cancellationToken);

    public async ValueTask<PluginLifecycleStoreBeginOutcome> BeginReplacementAsync(
        PluginLifecycleBeginRequest request,
        CancellationToken cancellationToken
    ) => await BeginAsync(request, replace: true, cancellationToken);

    private async ValueTask<PluginLifecycleStoreBeginOutcome> BeginAsync(
        PluginLifecycleBeginRequest request,
        bool replace,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.PluginLifecycles.SingleOrDefaultAsync(
            value => value.PluginId == request.Installation.PluginId.Value,
            cancellationToken
        );
        var current = record is null ? null : PluginLifecycleRecordMapper.ToDomain(record);
        var transition = replace
            ? PluginLifecycleStateMachine.BeginReplacement(
                current,
                request.Installation,
                request.OperationId,
                request.OccurredAtUtc
            )
            : PluginLifecycleStateMachine.BeginActivation(
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
        if (
            next.PluginId != expected.PluginId
            || next.Revision != expected.Revision + 1
            || !PluginLifecycleStateMachine.HasValidFaultInvariant(next)
        )
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

    public async ValueTask<PluginLifecycleStoreRemovalOutcome> CompleteRemovalAsync(
        PluginLifecycleState expected,
        PluginLifecycleOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        if (
            expected.Phase != PluginLifecyclePhase.Removing
            || outcome is not { Code: PluginLifecycleOutcomeCode.Removed, FailureCode: null }
        )
        {
            return new PluginLifecycleStoreRemovalOutcome.Conflict(
                await LoadAsync(expected.PluginId, cancellationToken)
            );
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.PluginLifecycles.SingleOrDefaultAsync(
            value => value.PluginId == expected.PluginId.Value,
            cancellationToken
        );
        if (record is null)
        {
            return new PluginLifecycleStoreRemovalOutcome.Completed(expected.PluginId);
        }

        var current = PluginLifecycleRecordMapper.ToDomain(record);
        if (current != expected)
        {
            return new PluginLifecycleStoreRemovalOutcome.Conflict(current);
        }

        _ = db.PluginLifecycles.Remove(record);
        try
        {
            _ = await db.SaveChangesAsync(cancellationToken);
            return new PluginLifecycleStoreRemovalOutcome.Completed(expected.PluginId);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveRemovalRaceAsync(expected.PluginId, cancellationToken);
        }
        catch (DbUpdateException)
        {
            var resolved = await ResolveRemovalRaceAsync(expected.PluginId, cancellationToken);
            if (resolved is PluginLifecycleStoreRemovalOutcome.Completed)
            {
                return resolved;
            }

            throw;
        }
    }

    private async ValueTask<PluginLifecycleStoreRemovalOutcome> ResolveRemovalRaceAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        var current = await LoadAsync(pluginId, cancellationToken);
        return current is null
            ? new PluginLifecycleStoreRemovalOutcome.Completed(pluginId)
            : new PluginLifecycleStoreRemovalOutcome.Conflict(current);
    }
}
