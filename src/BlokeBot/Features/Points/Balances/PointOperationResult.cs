namespace BlokeBot.Features.Points.Balances;

public enum PointOperationFailureReason
{
    None,
    InvalidAmount,
    UnknownUser,
    InsufficientBalance,
    CapExceeded,
    NotActive,
}

public sealed record PointOperationResult(
    bool Success,
    string Message,
    PointAmount? Balance = null,
    PointAmount? Amount = null,
    PointOperationFailureReason FailureReason = PointOperationFailureReason.None,
    TwitchCommandResponseTarget Target = TwitchCommandResponseTarget.Chat
)
{
    public static PointOperationResult Failure(
        PointOperationFailureReason reason,
        string message = "",
        PointAmount? balance = null,
        PointAmount? amount = null,
        TwitchCommandResponseTarget target = TwitchCommandResponseTarget.Chat
    ) => new(false, message, balance, amount, reason, target);

    public static PointOperationResult Successful(
        string message = "",
        PointAmount? balance = null,
        PointAmount? amount = null,
        TwitchCommandResponseTarget target = TwitchCommandResponseTarget.Chat
    ) => new(true, message, balance, amount, PointOperationFailureReason.None, target);
}
