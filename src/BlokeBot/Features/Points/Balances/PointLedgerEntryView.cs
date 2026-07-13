using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Balances;

public sealed record PointLedgerEntryView(
    DateTime CreatedAtUtc,
    PointLedgerKind Kind,
    string Login,
    string Delta,
    string BalanceAfter,
    string? ActorLogin,
    string? CounterpartyLogin,
    string Note
);
