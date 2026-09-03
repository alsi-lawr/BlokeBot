namespace BlokeBot.DatabaseCutover;

internal enum CutoverPhase
{
    DatabasePlanned,
    DatabaseCreated,
    SchemaReady,
    Prepared,
    Copying,
    RestoringSelfReferences,
    AdvancingSequences,
    Verifying,
    Verified,
    Complete,
}

internal sealed record CutoverTableCheckpoint(
    string Table,
    long RowsCopied,
    long SelfReferenceRowsRestored
);

internal sealed record CutoverReceipt(
    int FormatVersion,
    Guid OperationId,
    CutoverPhase Phase,
    string SqliteMigration,
    IReadOnlyList<CutoverTableRows> SourceRows,
    string LocalStateFingerprint,
    string PostgreSqlClusterIdentity,
    string PostgreSqlDatabase,
    string PostgreSqlOwner,
    IReadOnlyList<CutoverTableCheckpoint> Checkpoints,
    string? FailureCode,
    string? FailureReason,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc
)
{
    internal const int CurrentFormatVersion = 5;

    internal CutoverReceipt WithPhase(CutoverPhase phase) =>
        this with
        {
            Phase = phase,
            FailureCode = null,
            FailureReason = null,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    internal CutoverReceipt WithCheckpoint(
        CutoverTableCheckpoint checkpoint,
        CutoverPhase phase = CutoverPhase.Copying
    ) =>
        WithPhase(phase) with
        {
            Checkpoints = Checkpoints
                .Where(item => !StringComparer.Ordinal.Equals(item.Table, checkpoint.Table))
                .Append(checkpoint)
                .OrderBy(item => item.Table, StringComparer.Ordinal)
                .ToArray(),
        };

    // The phase stays at the last durable step so that a retry resumes from it.
    internal CutoverReceipt Failed(string code, string? reason) =>
        this with
        {
            FailureCode = code,
            FailureReason = reason,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    internal CutoverReceipt Completed() =>
        WithPhase(CutoverPhase.Complete) with
        {
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };
}
