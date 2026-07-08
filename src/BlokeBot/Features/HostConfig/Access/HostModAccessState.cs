namespace BlokeBot.Features.HostConfig.Access;

public sealed record HostModAccessState(
    bool ModsEnabled,
    IReadOnlyList<string> Whitelist,
    IReadOnlyList<string> Blacklist
);
