namespace BlokeBot.Persistence.Models;

public enum AccessListEntryKind
{
    Blacklist,
    Whitelist,
}

public static class AccessListEntryKindStore
{
    private const string BlacklistValue = "blacklist";
    private const string WhitelistValue = "whitelist";

    public static IReadOnlyList<string> Values { get; } = [BlacklistValue, WhitelistValue];

    public static string Format(AccessListEntryKind kind) =>
        kind switch
        {
            AccessListEntryKind.Blacklist => BlacklistValue,
            AccessListEntryKind.Whitelist => WhitelistValue,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static AccessListEntryKind Parse(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            BlacklistValue => AccessListEntryKind.Blacklist,
            WhitelistValue => AccessListEntryKind.Whitelist,
            _ => throw new FormatException($"Unknown access-list entry kind '{value}'."),
        };
}

public interface IAccessListEntry
{
    string Login { get; set; }

    AccessListEntryKind Kind { get; set; }

    DateTime CreatedAtUtc { get; set; }
}
