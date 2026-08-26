using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

internal sealed class PluginPrivateDataLifecycleOwner(
    PluginPrivateDataStore store,
    IPluginLifecycleMigrationRunner migrations,
    IPluginRuntimeSnapshotProvider runtime,
    TimeProvider timeProvider
) : IPluginMigrationDataOwner, IPluginRemovalDataOwner
{
    public async ValueTask<PluginLifecycleOwnerOutcome> MigrateAsync(
        PluginMigrationContext context,
        CancellationToken cancellationToken
    )
    {
        var package = context.Package;
        var manifest = package?.PreparedPackage.Manifest?.Manifest;
        if (
            manifest is null
            || package is null
            || manifest.Id != context.Installation.PluginId
            || manifest.Release != context.Installation.Release
            || !IsCurrentMigration(context)
        )
        {
            return Failed("Plugin private-data migration is not current.");
        }

        try
        {
            await using var database = await store.BeginMigrationAsync(
                context.Installation.PluginId,
                cancellationToken
            );
            var plan = MigrationPlan(
                database.CurrentVersion,
                context.Installation.Release.DeclaredVersion,
                manifest.Migrations
            );
            if (plan is null)
            {
                return Failed("Plugin private-data migration path is unavailable.");
            }

            if (plan.Value.Length > 0)
            {
                var started = await migrations.StartAsync(package, cancellationToken);
                if (started is not PluginLifecycleMigrationSessionOutcome.Started worker)
                {
                    return Failed("Plugin private-data migration worker could not start.");
                }

                await using var migrationWorker = worker.Session;
                foreach (var migration in plan.Value)
                {
                    if (!IsCurrentMigration(context))
                    {
                        return Failed("Plugin private-data migration became stale.");
                    }

                    var identity = Identity(context, migration);
                    database.Bind(identity.InvocationId);
                    PluginWorkerInvocationResult result;
                    try
                    {
                        result = await migrationWorker.InvokeAsync(
                            identity,
                            migration,
                            MigrationInput(migration),
                            cancellationToken
                        );
                    }
                    finally
                    {
                        database.Unbind(identity.InvocationId);
                    }

                    if (result.Outcome is not PluginWorkerInvocationOutcome.Returned)
                    {
                        return Failed("Plugin private-data migration callback failed.");
                    }
                }
            }

            if (!IsCurrentMigration(context))
            {
                return Failed("Plugin private-data migration became stale.");
            }

            await database.CommitAsync(
                context.Installation.Release.DeclaredVersion,
                cancellationToken
            );
            return new PluginLifecycleOwnerOutcome.Succeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed("Plugin private-data migration failed.");
        }
    }

    public async ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
        PluginRemovalContext context,
        CancellationToken cancellationToken
    )
    {
        if (!IsCurrent(context.PluginId, context.Fence, PluginLifecyclePhase.Removing))
        {
            return Failed("Plugin private-data removal is not current.");
        }

        await store.RemovePluginDataAsync(context.PluginId, cancellationToken);
        return IsCurrent(context.PluginId, context.Fence, PluginLifecyclePhase.Removing)
            ? new PluginLifecycleOwnerOutcome.Succeeded()
            : Failed("Plugin private-data removal became stale.");
    }

    private bool IsCurrent(
        PluginId pluginId,
        PluginLifecycleFence fence,
        PluginLifecyclePhase phase
    ) =>
        runtime.Current.Entries.TryGetValue(pluginId, out var entry)
        && entry.Fence == fence
        && entry.Phase == phase;

    private bool IsCurrentMigration(PluginMigrationContext context) =>
        runtime.Current.Entries.TryGetValue(context.Installation.PluginId, out var entry)
        && entry.Installation == context.Installation
        && entry.Fence == context.Fence
        && entry.Phase == PluginLifecyclePhase.Migrating;

    private PluginWorkerInvocationIdentity Identity(
        PluginMigrationContext context,
        PluginMigrationDescriptor migration
    )
    {
        _ = PluginFeatureId.TryCreate("migration", out var feature);
        _ = PluginHostId.TryCreate(1, out var host);
        _ = PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var invocationId);
        _ = PluginCoroutineId.TryCreate(Guid.NewGuid(), out var coroutineId);
        _ = PluginWorkerCancellationId.TryCreate(Guid.NewGuid(), out var cancellationId);
        _ = PluginActivationOperationId.TryCreate(
            context.Fence.OperationId.Value,
            out var operationId
        );
        _ = PluginFeatureActivationGeneration.TryCreate(1, out var featureGeneration);
        return new(
            context.Installation,
            feature,
            host,
            new PluginInvocationContext.Migration(
                context.Installation,
                migration.Id,
                migration.FromVersion,
                migration.ToVersion
            ),
            invocationId,
            coroutineId,
            context.Fence.Generation,
            PluginWorkerDeadline.From(
                timeProvider
                    .GetUtcNow()
                    .AddMilliseconds(PluginWorkerLimits.MaximumInvocationDurationMilliseconds)
            ),
            cancellationId,
            new(operationId, context.Fence.Generation, featureGeneration)
        );
    }

    private static ImmutableArray<PluginMigrationDescriptor>? MigrationPlan(
        SemanticVersion? current,
        SemanticVersion target,
        ImmutableArray<PluginMigrationDescriptor> declared
    )
    {
        if (current is null)
        {
            _ = SemanticVersion.TryCreate("0.0.0", out var zero);
            if (!declared.Any(migration => migration.FromVersion.HasSamePrecedenceAs(zero)))
            {
                return [];
            }
            current = zero;
        }

        if (current.CompareTo(target) > 0)
        {
            return null;
        }

        var plan = ImmutableArray.CreateBuilder<PluginMigrationDescriptor>();
        while (!current.HasSamePrecedenceAs(target))
        {
            var candidates = declared
                .Where(migration =>
                    migration.FromVersion.HasSamePrecedenceAs(current)
                    && migration.ToVersion.CompareTo(target) <= 0
                )
                .ToArray();
            if (candidates.Length != 1)
            {
                return null;
            }

            plan.Add(candidates[0]);
            current = candidates[0].ToVersion;
        }
        return plan.ToImmutable();
    }

    private static PluginValue MigrationInput(PluginMigrationDescriptor migration) =>
        new PluginValue.Map([
            new("migrationId", new PluginValue.String(migration.Id.Value)),
            new("fromVersion", new PluginValue.String(migration.FromVersion.Value)),
            new("toVersion", new PluginValue.String(migration.ToVersion.Value)),
        ]);

    private static PluginLifecycleOwnerOutcome.Failed Failed(string detail) =>
        new(
            PluginLifecycleOwnerFailureCode.Failed,
            PluginLifecycleSafeDetail.TryCreate(detail, out var safe) ? safe : null
        );
}
