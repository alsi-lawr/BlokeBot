using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.DatabaseWorkloads;

internal static partial class PostgreSqlAuthorityVerifier
{
    private static async Task<int[]> TwoWriterReceiptClaimAsync(
        DbContextOptions<BlokeBotDbContext> options,
        CancellationToken cancellationToken
    )
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = Enumerable
            .Range(0, 2)
            .Select(async writer =>
            {
                _ = writer;
                await start.Task;
                await using var db = new BlokeBotDbContext(options);
                return await MainDatabaseStatements.TryClaimAutomationEventReceiptAsync(
                    db,
                    1,
                    "synthetic.event",
                    "shared-message",
                    _now,
                    _now.AddMinutes(10),
                    cancellationToken
                );
            });
        start.SetResult();
        return await Task.WhenAll(writers);
    }

    private static async Task VerifyClaimBoundAsync(
        DbContextOptions<BlokeBotDbContext> options,
        CancellationToken cancellationToken
    )
    {
        await using var lockOwner = new BlokeBotDbContext(options);
        await using var held = await lockOwner.Database.BeginTransactionAsync(cancellationToken);
        Require(
            await MainDatabaseStatements.LockHostAsync(lockOwner, 1, cancellationToken) == 1,
            "claim-bound lock setup"
        );

        await using var contender = new BlokeBotDbContext(options);
        await using var transaction = await contender.Database.BeginTransactionAsync(
            cancellationToken
        );
        await MainDatabaseCommandTimeout.ApplyClaimBoundAsync(
            contender,
            TimeSpan.FromMilliseconds(100),
            cancellationToken
        );
        try
        {
            _ = await MainDatabaseStatements.LockHostAsync(contender, 1, cancellationToken);
            throw new InvalidDataException("Claim lock contention ignored its bound.");
        }
        catch (Exception exception)
            when (MainDatabaseFailureClassifier.Classify(exception, cancellationToken)
                == MainDatabaseFailureKind.LockTimeout
            ) { }
    }

    private static async Task VerifyContendedAdmissionAsync(
        DbContextOptions<BlokeBotDbContext> options,
        CancellationToken cancellationToken
    )
    {
        await using var lockOwner = new BlokeBotDbContext(options);
        await using var held = await MainDatabaseWriteTransaction.StartImmediateAsync(
            lockOwner,
            cancellationToken
        );

        await using var boundedWriter = new BlokeBotDbContext(options);
        try
        {
            _ = await MainDatabaseWriteTransaction.StartImmediateWithBoundedAdmissionAsync(
                boundedWriter,
                TimeSpan.FromMilliseconds(100),
                cancellationToken
            );
            throw new InvalidDataException("Bounded write admission ignored lock contention.");
        }
        catch (Exception exception)
            when (MainDatabaseFailureClassifier.Classify(exception, cancellationToken)
                == MainDatabaseFailureKind.LockTimeout
            ) { }

        using var callerCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await using var cancellableWriter = new BlokeBotDbContext(options);
        try
        {
            _ = await MainDatabaseWriteTransaction.StartImmediateAsync(
                cancellableWriter,
                callerCancellation.Token
            );
            throw new InvalidDataException("Cancelled write admission unexpectedly started.");
        }
        catch (Exception exception)
            when (MainDatabaseFailureClassifier.Classify(exception, callerCancellation.Token)
                == MainDatabaseFailureKind.CallerCancellation
            ) { }
    }

    private static async Task<int[]> TwoWriterImmediateWriteAsync(
        DbContextOptions<BlokeBotDbContext> options,
        CancellationToken cancellationToken
    )
    {
        var operationId = Guid.Parse("00000000-0000-0000-0000-000000000276");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = Enumerable
            .Range(0, 2)
            .Select(async writer =>
            {
                _ = writer;
                await start.Task;
                await using var db = new BlokeBotDbContext(options);
                await using var transaction =
                    await MainDatabaseWriteTransaction.StartImmediateAsync(db, cancellationToken);
                var changed = await db
                    .PluginFeatureStates.Where(value =>
                        value.PluginId == "synthetic-plugin"
                        && value.FeatureId == "serialized-feature"
                        && value.HostId == 1
                        && value.Revision == 1
                    )
                    .ExecuteUpdateAsync(
                        updates =>
                            updates
                                .SetProperty(value => value.Revision, 2)
                                .SetProperty(value => value.LifecycleOperationId, operationId),
                        cancellationToken
                    );
                if (changed == 1)
                {
                    _ = db.CommunityAudits.Add(
                        new()
                        {
                            HostId = 1,
                            Action = "SerializedWrite",
                            OperationKey = operationId.ToString("N"),
                            ActorTwitchUserId = "synthetic",
                            ActorLogin = "synthetic",
                            OccurredAtUtc = _now,
                        }
                    );
                    _ = await db.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                return changed;
            });
        start.SetResult();
        return await Task.WhenAll(writers);
    }
}
