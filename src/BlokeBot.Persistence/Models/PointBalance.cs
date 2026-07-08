namespace BlokeBot.Persistence.Models;

public sealed class PointBalance
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Login { get; set; } = string.Empty;

    public string Amount { get; set; } = "0";

    public DateTime UpdatedAtUtc { get; set; }
}
