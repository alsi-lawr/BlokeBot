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
    internal static IQueryable<string> ExcludedLogins(BlokeBotDbContext db, int hostId)
    {
        var hidden = HiddenPassports(db, hostId);
        return hidden
            .Where(value => value.Login.Trim() != "")
            .Select(value => value.Login)
            .Union(
                db.ViewerPassportLogins.Where(value =>
                        hidden.Any(passport => passport.Id == value.PassportId)
                    )
                    .Select(value => value.Login)
            )
            .Union(
                db.ViewerPassportAmbiguousLogins.Where(value => value.HostId == hostId)
                    .Select(value => value.Login)
            );
    }

    private static IQueryable<ViewerPassport> HiddenPassports(BlokeBotDbContext db, int hostId) =>
        db
            .ViewerPassports.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.Visibility != ViewerPassportVisibility.Public
                && db.Hosts.Any(host =>
                    host.Id == hostId
                    && (host.EnabledFeatures & HostFeatureFlags.ViewerPassports)
                        == HostFeatureFlags.ViewerPassports
                )
            );

    public async Task<ViewerPassportPublicExclusions> ExclusionsAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ambiguousLogins = await db
            .ViewerPassportAmbiguousLogins.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => value.Login)
            .ToArrayAsync(cancellationToken);
        var enabled = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId)
            .Select(value => value.EnabledFeatures)
            .SingleOrDefaultAsync(cancellationToken);
        if (!enabled.Contains(HostFeatureFlags.ViewerPassports))
        {
            return new(
                ViewerPassportPublicExclusions.None.TwitchUserIds,
                ambiguousLogins.ToHashSet(StringComparer.Ordinal)
            );
        }
        var hidden = await db
            .ViewerPassports.AsNoTracking()
            .Where(value =>
                value.HostId == hostId && value.Visibility != ViewerPassportVisibility.Public
            )
            .Select(value => new { value.TwitchUserId, value.Login })
            .ToArrayAsync(cancellationToken);
        var hiddenLogins = await (
            from login in db.ViewerPassportLogins.AsNoTracking()
            join passport in db.ViewerPassports.AsNoTracking()
                on login.PassportId equals passport.Id
            where
                passport.HostId == hostId && passport.Visibility != ViewerPassportVisibility.Public
            select login.Login
        ).ToArrayAsync(cancellationToken);
        return new(
            hidden.Select(value => value.TwitchUserId).ToHashSet(StringComparer.Ordinal),
            hidden
                .Where(value => !string.IsNullOrWhiteSpace(value.Login))
                .Select(value => value.Login)
                .Concat(hiddenLogins)
                .Concat(ambiguousLogins)
                .ToHashSet(StringComparer.Ordinal)
        );
    }
}
