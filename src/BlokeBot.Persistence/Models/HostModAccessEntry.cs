namespace BlokeBot.Persistence.Models;

public sealed class HostModAccessEntry : IAccessListEntry
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Login { get; set; } = string.Empty;

    public AccessListEntryKind Kind { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
