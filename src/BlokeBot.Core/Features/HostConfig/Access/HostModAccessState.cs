namespace BlokeBot.Core.Features.HostConfig.Access;

public sealed record HostModAccessState(
    bool ModsEnabled,
    bool AllowModsByDefault,
    IReadOnlyList<string> Whitelist,
    IReadOnlyList<string> Blacklist
);
