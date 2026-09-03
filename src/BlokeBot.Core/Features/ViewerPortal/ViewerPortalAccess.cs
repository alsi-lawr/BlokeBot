using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Core.Identity;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ViewerPortal;

/// <summary>The portal's channel resolution, viewer identity and passport access boundary.</summary>
public sealed class ViewerPortalAccess(
    PublicLeaderboardHostLookup hosts,
    ViewerPassportService passports,
    IDbContextFactory<BlokeBotDbContext> dbFactory
)
{
    // The route login is the only input: the host is never taken from the operator's dashboard
    // selection. Normalisation and the single exact match are the public leaderboards' lookup,
    // backed by the unique index on hosts.login.
    public async Task<PortalChannelOutcome> ResolveChannelAsync(
        string routeLogin,
        CancellationToken cancellationToken
    )
    {
        var host = await hosts.Find(routeLogin).RunAsync(cancellationToken);
        return host.Match<PortalChannelOutcome>(
            static value => new PortalChannelOutcome.Resolved(
                new PortalChannel(
                    new PortalHostKey(value.Id, value.Login),
                    value.DisplayName,
                    ViewerPortalFeatures.PublicFeatures(value.EnabledFeatures)
                )
            ),
            static () => new PortalChannelOutcome.NotFound()
        );
    }

    public static async Task<PortalIdentity> IdentifyAsync(
        Task<AuthenticationState>? authenticationState
    )
    {
        if (authenticationState is null)
        {
            return new PortalIdentity.UnavailableAuthentication();
        }
        try
        {
            var state = await authenticationState;
            return Identify(AuthenticatedSession.FromPrincipal(state.User));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new PortalIdentity.UnavailableAuthentication();
        }
    }

    public static PortalIdentity Identify(AuthenticatedSession session)
    {
        if (!session.IsAuthenticated)
        {
            return new PortalIdentity.Anonymous();
        }
        // A principal without a login, or whose host claims no longer decode, is what
        // AuthCookieValidator signs out on its next request; the portal treats it the same way now.
        var login = LoginName.Parse(session.Login).Value;
        return
            string.IsNullOrWhiteSpace(session.UserId)
            || login.Length == 0
            || session.State is AuthSessionState.Invalid
            ? new PortalIdentity.StaleSession()
            : new PortalIdentity.Authenticated(session.UserId, login, session.DisplayText);
    }

    // Identity is the Twitch user id; the session login is presentation. A passport row on this
    // host is read only to notice a rename or a retired login, never to grant or deny the binding.
    public async Task<PortalSelfOutcome> BindSelfAsync(
        PortalChannel channel,
        PortalIdentity identity,
        CancellationToken cancellationToken
    )
    {
        switch (identity)
        {
            case PortalIdentity.Anonymous:
                return new PortalSelfOutcome.Anonymous();
            case PortalIdentity.StaleSession:
                return new PortalSelfOutcome.StaleSession();
            case PortalIdentity.UnavailableAuthentication:
                return new PortalSelfOutcome.UnavailableAuthentication();
            case PortalIdentity.Authenticated authenticated:
                var viewer = new PortalViewer(
                    channel.Host,
                    authenticated.TwitchUserId,
                    authenticated.Login,
                    authenticated.DisplayName
                );
                await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
                {
                    var passport = await db
                        .ViewerPassports.AsNoTracking()
                        .Where(value =>
                            value.HostId == channel.Host.Id
                            && value.TwitchUserId == authenticated.TwitchUserId
                        )
                        .Select(value => new { value.Login })
                        .SingleOrDefaultAsync(cancellationToken);
                    if (passport is null)
                    {
                        // The erase sweep and a viewer reset both delete the passport and tombstone
                        // its logins, so a retired session login with no passport is an erased
                        // identity on this host until the viewer opts back in.
                        return await IsRetiredLoginAsync(
                            db,
                            channel.Host.Id,
                            authenticated.Login,
                            cancellationToken
                        )
                            ? new PortalSelfOutcome.Erased()
                            : new PortalSelfOutcome.AuthenticatedSelf(viewer);
                    }
                    return passport.Login.Length > 0 && passport.Login != authenticated.Login
                        ? new PortalSelfOutcome.Renamed(viewer)
                        : new PortalSelfOutcome.AuthenticatedSelf(viewer);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(identity));
        }
    }

    // The passport service decides visibility. The portal never elevates a channel manager: a
    // signed-in streamer or moderator is a public viewer here.
    public async Task<PortalPassportOutcome> OpenPassportAsync(
        PortalChannel channel,
        string viewerLogin,
        PortalIdentity audience,
        CancellationToken cancellationToken
    )
    {
        var login = LoginName.Parse(viewerLogin).Value;
        var passportAudience = audience is PortalIdentity.Authenticated authenticated
            ? new ViewerPassportAudience(authenticated.TwitchUserId, IsChannelManager: false)
            : ViewerPassportAudience.Anonymous;
        var outcome = await passports.GetVisibleAsync(
            channel.Host.Login,
            login,
            passportAudience,
            cancellationToken
        );
        return outcome switch
        {
            ViewerPassportQueryOutcome.Available available => new PortalPassportOutcome.Visible(
                available.Passport
            ),
            ViewerPassportQueryOutcome.FeatureDisabled =>
                new PortalPassportOutcome.FeatureDisabled(),
            ViewerPassportQueryOutcome.Forbidden => audience is PortalIdentity.Authenticated
                ? new PortalPassportOutcome.Unauthorized()
                : new PortalPassportOutcome.Hidden(),
            ViewerPassportQueryOutcome.NotFound => await ClassifyMissingAsync(
                channel.Host.Id,
                login,
                cancellationToken
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
    }

    private async Task<PortalPassportOutcome> ClassifyMissingAsync(
        int hostId,
        string login,
        CancellationToken cancellationToken
    )
    {
        if (login.Length == 0)
        {
            return new PortalPassportOutcome.NotFound();
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await IsRetiredLoginAsync(db, hostId, login, cancellationToken))
        {
            return new PortalPassportOutcome.Ambiguous();
        }
        var historical = await db
            .ViewerPassportLogins.AsNoTracking()
            .AnyAsync(value => value.HostId == hostId && value.Login == login, cancellationToken);
        return historical
            ? new PortalPassportOutcome.HistoricalLogin()
            : new PortalPassportOutcome.NotFound();
    }

    private static Task<bool> IsRetiredLoginAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        CancellationToken cancellationToken
    ) =>
        db
            .ViewerPassportAmbiguousLogins.AsNoTracking()
            .AnyAsync(value => value.HostId == hostId && value.Login == login, cancellationToken);
}
