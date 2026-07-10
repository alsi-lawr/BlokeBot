namespace BlokeBot.Persistence.Models;

public enum AccessListEntryKind
{
    [PersistedToken("blacklist")]
    Blacklist,

    [PersistedToken("whitelist")]
    Whitelist,
}

public interface IAccessListEntry
{
    string Login { get; set; }

    AccessListEntryKind Kind { get; set; }

    DateTime CreatedAtUtc { get; set; }
}
