namespace BlokeBot.Persistence.Models;

public sealed class PointsGiveaway
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Status { get; set; } = "Active";

    public DateTime StartedAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string MinimumPayout { get; set; } = "10";

    public string MaximumPayout { get; set; } = "100";

    public int WinnerCount { get; set; } = 1;

    public string Eligibility { get; set; } = "everyone";

    public ICollection<PointsGiveawayEntrant> Entrants { get; set; } = [];

    public ICollection<PointsGiveawayWinner> Winners { get; set; } = [];
}
