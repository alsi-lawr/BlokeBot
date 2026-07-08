namespace BlokeBot.Features.Points.Balances;

public sealed record PointLedgerEntryView(
    DateTime CreatedAtUtc,
    string Kind,
    string Login,
    string Delta,
    string BalanceAfter,
    string? ActorLogin,
    string? CounterpartyLogin,
    string Note
);
