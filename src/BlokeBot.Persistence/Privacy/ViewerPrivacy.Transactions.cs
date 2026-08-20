using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private sealed record TrackedEntrySnapshot(
        object Entity,
        EntityState State,
        PropertyValues CurrentValues,
        PropertyValues OriginalValues,
        TrackedPropertySnapshot[] Properties
    );

    private sealed record TrackedPropertySnapshot(string Name, bool IsModified, bool IsTemporary);

    private static async Task<T> ExecuteConsistentSnapshotAsync<T>(
        BlokeBotDbContext db,
        Func<T> safeResult,
        Func<Task<T>> operation,
        CancellationToken cancellationToken
    )
    {
        var ambient = db.Database.CurrentTransaction;
        if (ambient is not null)
        {
            return await ExecuteInAmbientTransactionAsync(
                db,
                ambient,
                safeResult,
                operation,
                cancellationToken
            );
        }

        var trackerSnapshot = CaptureTrackerSnapshot(db);
        IDbContextTransaction transaction;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken
            );
        }
        catch (Exception exception) when (IsSqliteSerializationFailure(exception))
        {
            RestoreTrackerSnapshot(db, trackerSnapshot);
            return safeResult();
        }

        await using (transaction)
        {
            try
            {
                var result = await operation();
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception)
            {
                var rolledBack = await TryRollbackTransactionAsync(transaction);
                RestoreTrackerSnapshot(db, trackerSnapshot);
                if (rolledBack && IsSqliteSerializationFailure(exception))
                {
                    return safeResult();
                }
                throw;
            }
        }
    }

    private static async Task<T> ExecuteInAmbientTransactionAsync<T>(
        BlokeBotDbContext db,
        IDbContextTransaction transaction,
        Func<T> safeResult,
        Func<Task<T>> operation,
        CancellationToken cancellationToken
    )
    {
        if (!transaction.SupportsSavepoints)
        {
            throw new NotSupportedException(
                "Viewer privacy operations require savepoint support inside an ambient transaction."
            );
        }

        var trackerSnapshot = CaptureTrackerSnapshot(db);
        var savepoint = $"ViewerPrivacy_{Guid.NewGuid():N}";
        try
        {
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        }
        catch (Exception exception) when (IsSqliteSerializationFailure(exception))
        {
            RestoreTrackerSnapshot(db, trackerSnapshot);
            return safeResult();
        }

        try
        {
            var result = await operation();
            await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            var rolledBack = await TryRollbackAndReleaseSavepointAsync(transaction, savepoint);
            RestoreTrackerSnapshot(db, trackerSnapshot);
            if (rolledBack && IsSqliteSerializationFailure(exception))
            {
                return safeResult();
            }
            throw;
        }
    }

    private static TrackedEntrySnapshot[] CaptureTrackerSnapshot(BlokeBotDbContext db)
    {
        var autoDetectChanges = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            return db
                .ChangeTracker.Entries()
                .Select(entry => new TrackedEntrySnapshot(
                    entry.Entity,
                    entry.State,
                    entry.CurrentValues.Clone(),
                    entry.OriginalValues.Clone(),
                    entry
                        .Properties.Select(property => new TrackedPropertySnapshot(
                            property.Metadata.Name,
                            property.IsModified,
                            property.IsTemporary
                        ))
                        .ToArray()
                ))
                .ToArray();
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    private static void RestoreTrackerSnapshot(
        BlokeBotDbContext db,
        IReadOnlyCollection<TrackedEntrySnapshot> snapshots
    )
    {
        var autoDetectChanges = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var originalEntities = snapshots
                .Select(snapshot => snapshot.Entity)
                .ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (
                var introduced in db
                    .ChangeTracker.Entries()
                    .Where(entry => !originalEntities.Contains(entry.Entity))
                    .ToArray()
            )
            {
                introduced.State = EntityState.Detached;
            }

            foreach (var snapshot in snapshots)
            {
                var entry = db.Entry(snapshot.Entity);
                if (entry.State == EntityState.Detached)
                {
                    entry.State =
                        snapshot.State == EntityState.Deleted
                            ? EntityState.Unchanged
                            : snapshot.State;
                }
                entry.CurrentValues.SetValues(snapshot.CurrentValues);
                entry.OriginalValues.SetValues(snapshot.OriginalValues);
                entry.State = snapshot.State;
                entry.CurrentValues.SetValues(snapshot.CurrentValues);
                entry.OriginalValues.SetValues(snapshot.OriginalValues);
                foreach (var propertySnapshot in snapshot.Properties)
                {
                    var property = entry.Property(propertySnapshot.Name);
                    property.IsTemporary = propertySnapshot.IsTemporary;
                    property.IsModified = propertySnapshot.IsModified;
                }
            }
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    private static async Task<bool> TryRollbackTransactionAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryRollbackAndReleaseSavepointAsync(
        IDbContextTransaction transaction,
        string savepoint
    )
    {
        try
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            return true;
        }
        catch
        {
            _ = await TryRollbackTransactionAsync(transaction);
            return false;
        }
    }

    private static bool IsSqliteSerializationFailure(Exception exception) =>
        exception
            is SqliteException
                {
                    SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED,
                }
                or DbUpdateException
                {
                    InnerException: SqliteException
                    {
                        SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED,
                    },
                };
}
