namespace BlokeBot.Persistence.Models;

public sealed class PointsGiveawayEntrant
{
    public int Id { get; set; }

    public int GiveawayId { get; set; }

    public PointsGiveaway? Giveaway { get; set; }

    public string Login { get; set; } = string.Empty;

    public DateTime JoinedAtUtc { get; set; }
}
