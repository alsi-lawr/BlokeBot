namespace BlokeBot.Persistence.Models;

public sealed class SiteAccessEntry
{
    public int Id { get; set; }

    public string Login { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
