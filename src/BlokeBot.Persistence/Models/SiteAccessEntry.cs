namespace BlokeBot.Persistence.Models;

public sealed class SiteAccessEntry : IAccessListEntry
{
    public int Id { get; set; }

    public string Login { get; set; } = string.Empty;

    public AccessListEntryKind Kind { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
