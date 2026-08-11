using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ViewerPassports;

public sealed record ViewerPassportPublicExclusions(
    IReadOnlySet<string> TwitchUserIds,
    IReadOnlySet<string> Logins
)
{
    public static ViewerPassportPublicExclusions None { get; } =
        new(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal)
        );
}

public sealed class ViewerPassportPublicIdentityPolicy(
    IDbContextFactory<BlokeBotDbContext> dbFactory
)
{
    public async Task<ViewerPassportPublicExclusions> ExclusionsAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var enabled = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId)
            .Select(value => value.EnabledFeatures)
            .SingleOrDefaultAsync(cancellationToken);
        if (!enabled.Contains(HostFeatureFlags.ViewerPassports))
        {
            return ViewerPassportPublicExclusions.None;
        }
        var hidden = await db
            .ViewerPassports.AsNoTracking()
            .Where(value =>
                value.HostId == hostId && value.Visibility != ViewerPassportVisibility.Public
            )
            .Select(value => new { value.TwitchUserId, value.Login })
            .ToArrayAsync(cancellationToken);
        return new(
            hidden.Select(value => value.TwitchUserId).ToHashSet(StringComparer.Ordinal),
            hidden
                .Where(value => !string.IsNullOrWhiteSpace(value.Login))
                .Select(value => value.Login)
                .ToHashSet(StringComparer.Ordinal)
        );
    }
}
