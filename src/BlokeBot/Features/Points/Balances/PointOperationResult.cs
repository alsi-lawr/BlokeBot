namespace BlokeBot.Features.Points.Balances;

public sealed record PointOperationResult(
    bool Success,
    string Message,
    PointAmount? Balance = null,
    PointAmount? Amount = null
);
