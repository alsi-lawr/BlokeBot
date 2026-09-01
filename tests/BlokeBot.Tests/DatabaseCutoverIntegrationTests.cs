using BlokeBot.DatabaseCutover;
using Shouldly;

namespace BlokeBot.Tests;

public sealed class DatabaseCutoverIntegrationTests
{
    [Test, DatabaseCutoverIntegration]
    public async Task Cutover_ResumesBoundedSelfReferenceRestorationWithPreservedState()
    {
        await using var fixture = await DatabaseCutoverIntegrationFixture.CreateAsync();
        await fixture.AssertTargetsHaveDistinctClusterIdentityAsync();
        var options = fixture.Options();
        using var interruption = new CancellationTokenSource();
        var interrupted = 0;
        var runner = new DatabaseCutoverRunner(batch =>
        {
            if (
                StringComparer.Ordinal.Equals(batch.Table, "hosts")
                && batch.Phase == CutoverBatchPhase.Copy
                && Interlocked.Exchange(ref interrupted, 1) == 0
            )
            {
                interruption.Cancel();
            }
        });

        var first = await runner.RunAsync(options, interruption.Token);

        _ = first.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        var receiptAfterInterruption = await new CutoverReceiptStore(
            fixture.StateDirectory
        ).ReadAsync(CancellationToken.None);
        _ = receiptAfterInterruption.ShouldNotBeNull();
        receiptAfterInterruption.OperationId.ShouldBe(fixture.OperationId);
        var checkpointedHosts =
            receiptAfterInterruption
                .Checkpoints.SingleOrDefault(checkpoint => checkpoint.Table == "hosts")
                ?.RowsCopied
            ?? 0;
        (await fixture.DomainRowCountAsync(fixture.Primary, "hosts")).ShouldBeGreaterThan(
            checkpointedHosts
        );

        await fixture.SelectTargetAsync(fixture.Other);
        var mismatched = await new DatabaseCutoverRunner().RunAsync(
            options,
            CancellationToken.None
        );

        _ = mismatched.ShouldBeOfType<DatabaseCutoverResult.Rejected>();
        ((DatabaseCutoverResult.Rejected)mismatched).Message.ShouldContain(
            "does not match the external cutover receipt"
        );
        (await fixture.DomainRowCountAsync(fixture.Other, "hosts")).ShouldBe(0);

        await fixture.SelectTargetAsync(fixture.Primary);
        using var selfReferenceInterruption = new CancellationTokenSource();
        var selfReferenceInterrupted = 0;
        var selfReferenceRunner = new DatabaseCutoverRunner(batch =>
        {
            if (
                StringComparer.Ordinal.Equals(batch.Table, "request_submissions")
                && batch.Phase == CutoverBatchPhase.SelfReferenceRestoration
                && Interlocked.Exchange(ref selfReferenceInterrupted, 1) == 0
            )
            {
                selfReferenceInterruption.Cancel();
            }
        });

        var selfReferenceFailure = await selfReferenceRunner.RunAsync(
            options,
            selfReferenceInterruption.Token
        );

        _ = selfReferenceFailure.ShouldBeOfType<DatabaseCutoverResult.Failed>();
        var receiptBeforeSelfReferenceResume = await new CutoverReceiptStore(
            fixture.StateDirectory
        ).ReadAsync(CancellationToken.None);
        _ = receiptBeforeSelfReferenceResume.ShouldNotBeNull();
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
        var completedReceipt = await new CutoverReceiptStore(fixture.StateDirectory).ReadAsync(
            CancellationToken.None
        );
        _ = completedReceipt.ShouldNotBeNull();
        var completedRequestCheckpoint = completedReceipt.Checkpoints.Single(checkpoint =>
            checkpoint.Table == "request_submissions"
        );
        completedRequestCheckpoint.SelfReferenceRowsRestored.ShouldBe(
            completedRequestCheckpoint.RowsCopied
        );
        await fixture.AssertTransferredStateAsync();
        await fixture.AssertProviderMetadataWasNotCopiedAsync();
        fixture.AssertLocalStateUnchanged();

        var repeated = await new DatabaseCutoverRunner().RunAsync(options, CancellationToken.None);

        repeated.ShouldBeOfType<DatabaseCutoverResult.Succeeded>().AlreadyComplete.ShouldBeTrue();
        await fixture.AssertPendingWorkPreservedAsync();
        await fixture.StartPostgreSqlAndWriteAsync();
        await fixture.AssertPostgreSqlWriteAndSequenceAsync();
        fixture.AssertLocalStateUnchanged();
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
