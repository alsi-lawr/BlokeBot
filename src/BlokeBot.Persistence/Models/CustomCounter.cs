namespace BlokeBot.Persistence.Models;

public sealed class CustomCounter
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Name { get; set; } = string.Empty;

    public long Value { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
