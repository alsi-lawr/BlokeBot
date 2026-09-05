using BlokeBot.DatabaseCutover;
using Shouldly;

namespace BlokeBot.Tests;

public sealed class DatabaseCutoverIntegrationTests
{
    [Test, DatabaseCutoverIntegration]
    public async Task Cutover_PreparesTargetAndResumesAcrossInjectedFailures()
    {
        await using var fixture = await DatabaseCutoverIntegrationFixture.CreateAsync();
        await fixture.AssertTargetsHaveDistinctClusterIdentityAsync();
        var options = fixture.Options();

        await MigrateSqliteBeforeAnyTargetMutationAsync(fixture, options);
        await RejectUnrelatedTargetsAsync(fixture, options);
        await PrepareTargetAcrossInjectedFailuresAsync(fixture, options);
        await CopyAcrossInjectedFailuresAsync(fixture, options);
        await SwitchAfterExplicitConfigurationChangeAsync(fixture);
    }

    private static async Task MigrateSqliteBeforeAnyTargetMutationAsync(
        DatabaseCutoverIntegrationFixture fixture,
        DatabaseCutoverOptions options
    )
    {
        (await fixture.SqliteMigrationsAsync())
            .Last()
            .ShouldBe(DatabaseCutoverIntegrationFixture.PriorReleaseSqliteMigration);

        var beforeReceipt = await RunUntilAsync(
            options,
            CutoverPreparationCheckpoint.BeforeReceipt
        );

        _ = beforeReceipt.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        (await fixture.SqliteMigrationsAsync())
            .Last()
            .ShouldBe(DatabaseCutoverIntegrationFixture.CurrentSqliteMigration);
        File.Exists(fixture.ReceiptPath).ShouldBeFalse();
        (await fixture.TargetDatabaseStateAsync(fixture.Primary)).ShouldBeNull();
        await fixture.SeedCurrentReleaseRowsAsync();
    }

    private static async Task RejectUnrelatedTargetsAsync(
        DatabaseCutoverIntegrationFixture fixture,
        DatabaseCutoverOptions options
    )
    {
        await fixture.CreateDatabaseByHandAsync(fixture.Primary);
        var existingDatabase = await new DatabaseCutoverRunner().RunAsync(
            options,
            CancellationToken.None
        );
        existingDatabase
            .ShouldBeOfType<DatabaseCutoverResult.Rejected>()
            .Message.ShouldContain("already exists without a matching external cutover receipt");
        File.Exists(fixture.ReceiptPath).ShouldBeFalse();
        (await fixture.TargetTablesAsync(fixture.Primary)).ShouldBeEmpty();
        await fixture.DropDatabaseByHandAsync(fixture.Primary);

        var afterReceipt = await RunUntilAsync(
            options,
            CutoverPreparationCheckpoint.ReceiptWritten
        );
        _ = afterReceipt.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        var receipt = AssertReceipt(
            await fixture.ReadReceiptAsync(),
            CutoverPhase.DatabasePlanned,
            "cancelled"
        );
        receipt.OperationId.ShouldBe(fixture.OperationId);
        receipt.SourceRows.ShouldContain(rows => rows.Table == "hosts" && rows.Rows == 1);
        await fixture.AssertReceiptRedactedAsync();
        (await fixture.TargetDatabaseStateAsync(fixture.Primary)).ShouldBeNull();

        await fixture.SelectTargetAsync(fixture.Other);
        await AssertRejectedWithoutCreationAsync(fixture, options, fixture.Other);
        await fixture.SelectOtherDatabaseAsync(fixture.Primary);
        await AssertRejectedWithoutCreationAsync(fixture, options, fixture.Primary);
        (await fixture.TargetDatabaseStateAsync(fixture.Primary, "blokebot_other")).ShouldBeNull();
        await fixture.SelectOtherOwnerAsync(fixture.Primary);
        await AssertRejectedWithoutCreationAsync(fixture, options, fixture.Primary);
        await fixture.SelectTargetAsync(fixture.Primary);
        (await fixture.ReadReceiptAsync())
            .ShouldNotBeNull()
            .Phase.ShouldBe(CutoverPhase.DatabasePlanned);
    }

    private static async Task AssertRejectedWithoutCreationAsync(
        DatabaseCutoverIntegrationFixture fixture,
        DatabaseCutoverOptions options,
        DatabaseCutoverIntegrationFixture.DisposablePostgreSql target
    )
    {
        var mismatched = await new DatabaseCutoverRunner().RunAsync(
            options,
            CancellationToken.None
        );

        mismatched
            .ShouldBeOfType<DatabaseCutoverResult.Rejected>()
            .Message.ShouldContain("does not match the external cutover receipt");
        (await fixture.TargetDatabaseStateAsync(target)).ShouldBeNull();
    }

    private static async Task PrepareTargetAcrossInjectedFailuresAsync(
        DatabaseCutoverIntegrationFixture fixture,
        DatabaseCutoverOptions options
    )
    {
        var afterCreate = await RunUntilAsync(
            options,
            CutoverPreparationCheckpoint.DatabaseCreated
        );
        _ = afterCreate.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        _ = AssertReceipt(
            await fixture.ReadReceiptAsync(),
            CutoverPhase.DatabasePlanned,
            "cancelled"
        );
        var created = (await fixture.TargetDatabaseStateAsync(fixture.Primary)).ShouldNotBeNull();
        created.Owner.ShouldBe("cutover");
        created.Comment.ShouldBe(fixture.ExpectedMarker);
        (await fixture.TargetTablesAsync(fixture.Primary)).ShouldBeEmpty();

        await fixture.ChangeOwnerByHandAsync(fixture.Primary, restore: false);
        var switchedOwner = await new DatabaseCutoverRunner().RunAsync(
            options,
            CancellationToken.None
        );
        switchedOwner
            .ShouldBeOfType<DatabaseCutoverResult.Rejected>()
            .Message.ShouldContain("owner does not match");
        await fixture.ChangeOwnerByHandAsync(fixture.Primary, restore: true);

        await fixture.CreateStrayTableAsync(fixture.Primary);
        var duringMigration = await new DatabaseCutoverRunner().RunAsync(
            options,
            CancellationToken.None
        );
        _ = duringMigration.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        _ = AssertReceipt(
            await fixture.ReadReceiptAsync(),
            CutoverPhase.DatabaseCreated,
            "unexpected-failure"
        );
        (await fixture.TargetMigrationsAsync(fixture.Primary)).ShouldBeEmpty();
        (await fixture.TargetTablesAsync(fixture.Primary)).ShouldBe(["hosts"]);
        await fixture.DropStrayTableAsync(fixture.Primary);

        var afterSchema = await RunUntilAsync(options, CutoverPreparationCheckpoint.SchemaApplied);
        _ = afterSchema.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        _ = AssertReceipt(
            await fixture.ReadReceiptAsync(),
            CutoverPhase.DatabaseCreated,
            "cancelled"
        );
        (await fixture.TargetMigrationsAsync(fixture.Primary)).ShouldBe(
            DatabaseCutoverIntegrationFixture.CurrentPostgreSqlMigrations
        );
        await fixture.AssertDomainTablesEmptyAsync(fixture.Primary);

        var strayHost = await DatabaseCutoverIntegrationFixture.InsertHostAsync(
            fixture.TargetConfiguration
        );
        var unrelatedData = await new DatabaseCutoverRunner().RunAsync(
            options,
            CancellationToken.None
        );
        unrelatedData
            .ShouldBeOfType<DatabaseCutoverResult.Rejected>()
            .Message.ShouldContain("contains data before the copy phase");
        await DatabaseCutoverIntegrationFixture.DeleteHostAsync(
            fixture.TargetConfiguration,
            strayHost
        );

        var afterBinding = await RunUntilAsync(options, CutoverPreparationCheckpoint.TargetBound);
        _ = afterBinding.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        var prepared = AssertReceipt(
            await fixture.ReadReceiptAsync(),
            CutoverPhase.Prepared,
            "cancelled"
        );
        prepared.Checkpoints.ShouldBeEmpty();
        await fixture.AssertDomainTablesEmptyAsync(fixture.Primary);
        await fixture.AssertReceiptRedactedAsync();
    }

    private static async Task CopyAcrossInjectedFailuresAsync(
        DatabaseCutoverIntegrationFixture fixture,
        DatabaseCutoverOptions options
    )
    {
        var first = await RunUntilBatchAsync(options, "hosts", CutoverBatchPhase.Copy);

        _ = first.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        var receiptAfterInterruption = AssertReceipt(
            await fixture.ReadReceiptAsync(),
            CutoverPhase.Copying,
            "cancelled"
        );
        receiptAfterInterruption.OperationId.ShouldBe(fixture.OperationId);
        var checkpointedHosts =
            receiptAfterInterruption
                .Checkpoints.SingleOrDefault(checkpoint => checkpoint.Table == "hosts")
                ?.RowsCopied
            ?? 0;
        (await fixture.DomainRowCountAsync(fixture.Primary, "hosts")).ShouldBeGreaterThan(
            checkpointedHosts
        );

        var strayTargetHost = await DatabaseCutoverIntegrationFixture.InsertHostAsync(
            fixture.TargetConfiguration
        );
        var strayRejected = await new DatabaseCutoverRunner().RunAsync(
            options,
            CancellationToken.None
        );

        strayRejected
            .ShouldBeOfType<DatabaseCutoverResult.Rejected>()
            .Message.ShouldBe("The PostgreSql target contains unrelated data in table hosts.");
        _ = AssertReceipt(
            await fixture.ReadReceiptAsync(),
            CutoverPhase.Copying,
            "target-reconciliation-failed"
        );
        await DatabaseCutoverIntegrationFixture.DeleteHostAsync(
            fixture.TargetConfiguration,
            strayTargetHost
        );

        var changedSourceHost = await DatabaseCutoverIntegrationFixture.InsertHostAsync(
            fixture.SourceConfiguration
        );
        var changedSourceRejected = await new DatabaseCutoverRunner().RunAsync(
            options,
            CancellationToken.None
        );

        changedSourceRejected
            .ShouldBeOfType<DatabaseCutoverResult.Rejected>()
            .Message.ShouldBe(
                "The source, target, or local state does not match the external cutover receipt."
            );
        await DatabaseCutoverIntegrationFixture.DeleteHostAsync(
            fixture.SourceConfiguration,
            changedSourceHost
        );

        var selfReferenceFailure = await RunUntilBatchAsync(
            options,
            "request_submissions",
            CutoverBatchPhase.SelfReferenceRestoration
        );

        _ = selfReferenceFailure.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        var receiptBeforeSelfReferenceResume = (await fixture.ReadReceiptAsync()).ShouldNotBeNull();
        var requestCheckpoint = receiptBeforeSelfReferenceResume.Checkpoints.Single(checkpoint =>
            checkpoint.Table == "request_submissions"
        );
        requestCheckpoint.RowsCopied.ShouldBe(2);
        requestCheckpoint.SelfReferenceRowsRestored.ShouldBe(0);
        (await fixture.MergedSubmissionTargetAsync()).ShouldBe(
            DatabaseCutoverIntegrationFixture.TargetSubmissionId
        );

        var resumed = await new DatabaseCutoverRunner().RunAsync(options, CancellationToken.None);

        var completed = resumed.ShouldBeOfType<DatabaseCutoverResult.Succeeded>();
        completed.OperationId.ShouldBe(fixture.OperationId);
        completed.AlreadyComplete.ShouldBeFalse();
        var completedReceipt = (await fixture.ReadReceiptAsync()).ShouldNotBeNull();
        var completedRequestCheckpoint = completedReceipt.Checkpoints.Single(checkpoint =>
            checkpoint.Table == "request_submissions"
        );
        completedRequestCheckpoint.SelfReferenceRowsRestored.ShouldBe(
            completedRequestCheckpoint.RowsCopied
        );
        await fixture.AssertSelfReferencesRestoredAsync();
        await fixture.AssertProviderMetadataWasNotCopiedAsync();
        fixture.AssertLocalStateUnchanged();
        await fixture.AssertReceiptRedactedAsync();

        var repeated = await new DatabaseCutoverRunner().RunAsync(options, CancellationToken.None);

        repeated.ShouldBeOfType<DatabaseCutoverResult.Succeeded>().AlreadyComplete.ShouldBeTrue();
        (await fixture.SqliteMigrationsAsync())
            .Last()
            .ShouldBe(DatabaseCutoverIntegrationFixture.CurrentSqliteMigration);
    }

    private static async Task SwitchAfterExplicitConfigurationChangeAsync(
        DatabaseCutoverIntegrationFixture fixture
    )
    {
        await fixture.AssertPendingWorkPreservedAsync();
        await fixture.StartPostgreSqlAndWriteAsync();
        await fixture.AssertPostgreSqlWriteAndSequenceAsync();
        await fixture.DeliverTransferredPendingWorkOnceAsync();
        await fixture.AssertTransferredPendingWorkDoesNotReplayAsync();
        fixture.AssertLocalStateUnchanged();
    }

    private static CutoverReceipt AssertReceipt(
        CutoverReceipt? receipt,
        CutoverPhase phase,
        string failureCode
    )
    {
        var current = receipt.ShouldNotBeNull();
        current.Phase.ShouldBe(phase);
        current.FailureCode.ShouldBe(failureCode);
        return current;
    }

    private static async Task<DatabaseCutoverResult> RunUntilAsync(
        DatabaseCutoverOptions options,
        CutoverPreparationCheckpoint checkpoint
    )
    {
        using var interruption = new CancellationTokenSource();
        var runner = new DatabaseCutoverRunner(
            null,
            reached =>
            {
                if (reached == checkpoint)
                {
                    interruption.Cancel();
                }
            }
        );
        return await runner.RunAsync(options, interruption.Token);
    }

    private static async Task<DatabaseCutoverResult> RunUntilBatchAsync(
        DatabaseCutoverOptions options,
        string table,
        CutoverBatchPhase phase
    )
    {
        using var interruption = new CancellationTokenSource();
        var interrupted = 0;
        var runner = new DatabaseCutoverRunner(
            batch =>
            {
                if (
                    StringComparer.Ordinal.Equals(batch.Table, table)
                    && batch.Phase == phase
                    && Interlocked.Exchange(ref interrupted, 1) == 0
                )
                {
                    interruption.Cancel();
                }
            },
            null
        );
        return await runner.RunAsync(options, interruption.Token);
    }
}

internal sealed class DatabaseCutoverIntegrationAttribute()
    : SkipAttribute(
        "Set BLOKEBOT_RUN_DATABASE_CUTOVER_INTEGRATION=1 to run the disposable PostgreSql cutover journey."
    )
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(
            !StringComparer.Ordinal.Equals(
                Environment.GetEnvironmentVariable("BLOKEBOT_RUN_DATABASE_CUTOVER_INTEGRATION"),
                "1"
            )
        );
}
