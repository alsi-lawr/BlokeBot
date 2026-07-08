namespace BlokeBot.Features.Points.Balances;

public sealed record PointBalanceEntry(string Login, PointAmount Balance, DateTime UpdatedAtUtc);
