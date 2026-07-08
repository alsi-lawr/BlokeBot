namespace BlokeBot.Persistence.Models;

public sealed class PointsGiveawayWinner
{
    public int Id { get; set; }

    public int GiveawayId { get; set; }

    public PointsGiveaway? Giveaway { get; set; }

    public string Login { get; set; } = string.Empty;

    public string Payout { get; set; } = "0";
}
