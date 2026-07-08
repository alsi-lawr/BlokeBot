namespace BlokeBot.Features.SiteAccess;

public sealed record SiteAccessAdminState(
    bool WhitelistEnabled,
    IReadOnlyList<string> Whitelist,
    IReadOnlyList<string> Blacklist
);
