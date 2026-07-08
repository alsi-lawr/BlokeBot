namespace BlokeBot.Features.Points.Balances;

public enum PointOperationFailureReason
{
    None,
    InvalidAmount,
    InsufficientBalance,
    CapExceeded,
    NotActive,
}

public sealed record PointOperationResult(
    bool Success,
    string Message,
    PointAmount? Balance = null,
    PointAmount? Amount = null,
    PointOperationFailureReason FailureReason = PointOperationFailureReason.None
)
{
    public static PointOperationResult Failure(
        PointOperationFailureReason reason,
        string message = "",
        PointAmount? balance = null,
        PointAmount? amount = null
    ) => new(false, message, balance, amount, reason);

    public static PointOperationResult Successful(
        string message = "",
        PointAmount? balance = null,
        PointAmount? amount = null
    ) => new(true, message, balance, amount);
}
