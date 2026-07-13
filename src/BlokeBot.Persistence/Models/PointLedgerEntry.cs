namespace BlokeBot.Persistence.Models;

public sealed class PointLedgerEntry
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public required PointLedgerKind Kind { get; set; }

    public string Login { get; set; } = string.Empty;

    public string Delta { get; set; } = "0";

    public string BalanceAfter { get; set; } = "0";

    public string? ActorLogin { get; set; }

    public string? CounterpartyLogin { get; set; }

    public int? GiveawayId { get; set; }

    public string Note { get; set; } = string.Empty;
}
