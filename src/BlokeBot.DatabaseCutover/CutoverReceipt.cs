namespace BlokeBot.DatabaseCutover;

internal enum CutoverPhase
{
    Prepared,
    Copying,
    RestoringSelfReferences,
    AdvancingSequences,
    Verifying,
    Verified,
    Failed,
    Complete,
}

internal sealed record CutoverTableCheckpoint(
    string Table,
    long RowsCopied,
    string PrefixHash,
    long SelfReferenceRowsRestored
);

internal sealed record CutoverReceipt(
    int FormatVersion,
    Guid OperationId,
    CutoverPhase Phase,
    string SourceFingerprint,
    string TargetFingerprint,
    string LocalStateFingerprint,
    IReadOnlyList<CutoverTableCheckpoint> Checkpoints,
    string? VerificationFingerprint,
    string? FailureCode,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc
)
{
    internal const int CurrentFormatVersion = 3;

    internal CutoverReceipt WithPhase(CutoverPhase phase) =>
        this with
        {
            Phase = phase,
            FailureCode = null,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    internal CutoverReceipt WithCheckpoint(
        CutoverTableCheckpoint checkpoint,
        CutoverPhase phase = CutoverPhase.Copying
    ) =>
        this with
        {
            Phase = phase,
            Checkpoints = Checkpoints
                .Where(item => !StringComparer.Ordinal.Equals(item.Table, checkpoint.Table))
                .Append(checkpoint)
                .OrderBy(item => item.Table, StringComparer.Ordinal)
                .ToArray(),
            FailureCode = null,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    internal CutoverReceipt Failed(string code) =>
        this with
        {
            Phase = CutoverPhase.Failed,
            FailureCode = code,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    internal CutoverReceipt Verified(string fingerprint) =>
        this with
        {
            Phase = CutoverPhase.Verified,
            VerificationFingerprint = fingerprint,
            FailureCode = null,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    internal CutoverReceipt Completed() =>
        this with
        {
            Phase = CutoverPhase.Complete,
            FailureCode = null,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };
}
