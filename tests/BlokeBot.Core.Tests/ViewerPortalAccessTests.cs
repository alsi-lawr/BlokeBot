using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Core.Features.ViewerPortal;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPortalAccessTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RawCasedSiblingHost_ResolvingChannel_MatchesOnlyTheNormalizedLogin()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var canonical = await SeedHostAsync(
            database,
            "streamer",
            HostFeatureFlags.Automations | HostFeatureFlags.Bingo | HostFeatureFlags.Points
        );
        _ = await SeedHostAsync(database, "Streamer", HostFeatureFlags.Bounties);
        var (access, _) = CreateAccess(database);

        var resolved = await access.ResolveChannelAsync("@STREAMER", default);
        var missing = await access.ResolveChannelAsync("missing", default);
        var empty = await access.ResolveChannelAsync("#", default);

        var channel = resolved.ShouldBeOfType<PortalChannelOutcome.Resolved>().Channel;
        channel.Host.ShouldBe(new PortalHostKey(canonical, "streamer"));
        channel.PublicFeatures.ShouldBe([HostFeatureFlags.Points, HostFeatureFlags.Bingo]);
        _ = missing.ShouldBeOfType<PortalChannelOutcome.NotFound>();
        _ = empty.ShouldBeOfType<PortalChannelOutcome.NotFound>();
        var owning = await new PublicLeaderboardHostLookup(database)
            .Find("@STREAMER")
            .RunAsync(default);
        owning.Match(static host => host.Id, static () => 0).ShouldBe(canonical);
    }

    [Test]
    public async Task AuthenticatedViewer_BindingSelf_MatchesThePassportServiceAndStaysPrivate()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var (access, passports) = CreateAccess(database);
        _ = Success(await passports.SaveAsync(Save(hostId, "viewer-id", "viewer"), default));
        var channel = await ResolveAsync(access, "channel");
        var identity = await ViewerPortalAccess.IdentifyAsync(
            State(Principal("viewer-id", "viewer"))
        );

        var outcome = await access.BindSelfAsync(channel, identity, default);

        identity.Presentation.ShouldBe(PortalIdentityPresentation.Authenticated);
        var viewer = outcome.ShouldBeOfType<PortalSelfOutcome.AuthenticatedSelf>().Viewer;
        viewer.ShouldBe(new PortalViewer(channel.Host, "viewer-id", "viewer", "viewer"));
        var self = (
            await passports.GetSelfAsync(
                hostId,
                new(viewer.TwitchUserId, viewer.Login, viewer.DisplayName),
                default
            )
        ).ShouldBeOfType<ViewerPassportQueryOutcome.Available>();
        self.Passport.HostId.ShouldBe(channel.Host.Id);
        self.Passport.TwitchUserId.ShouldBe(viewer.TwitchUserId);
        var scope = PortalCacheScope
            .For(channel.Host, identity)
            .ShouldBeOfType<PortalCacheScope.Private>();
        scope.CacheControl.ShouldBe("no-store");
    }

    [Test]
    public async Task RenamedViewer_BindingSelf_KeepsTheTwitchIdentityAndRetiresTheOldLoginText()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var (access, passports) = CreateAccess(database);
        _ = Success(await passports.SaveAsync(Save(hostId, "viewer-id", "old_login"), default));
        _ = Success(await passports.SaveAsync(Save(hostId, "viewer-id", "new_login"), default));
        var channel = await ResolveAsync(access, "channel");

        var renamedAgain = await access.BindSelfAsync(
            channel,
            Identify("viewer-id", "newer_login"),
            default
        );
        var current = await access.BindSelfAsync(
            channel,
            Identify("viewer-id", "new_login"),
            default
        );
        var oldLogin = await access.OpenPassportAsync(
            channel,
            "old_login",
            new PortalIdentity.Anonymous(),
            default
        );
        var unknown = await access.OpenPassportAsync(
            channel,
            "never_seen",
            new PortalIdentity.Anonymous(),
            default
        );

        renamedAgain
            .ShouldBeOfType<PortalSelfOutcome.Renamed>()
            .Viewer.ShouldBe(
                new PortalViewer(channel.Host, "viewer-id", "newer_login", "newer_login")
            );
        current
            .ShouldBeOfType<PortalSelfOutcome.AuthenticatedSelf>()
            .Viewer.TwitchUserId.ShouldBe("viewer-id");
        _ = oldLogin.ShouldBeOfType<PortalPassportOutcome.HistoricalLogin>();
        _ = unknown.ShouldBeOfType<PortalPassportOutcome.NotFound>();
        _ = (
            await passports.GetVisibleAsync(
                "channel",
                "old_login",
                ViewerPassportAudience.Anonymous,
                default
            )
        ).ShouldBeOfType<ViewerPassportQueryOutcome.NotFound>();
    }

    [Test]
    public async Task HiddenPassport_OpeningThroughThePortal_ExposesNothingAndNeverElevatesManagers()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var (access, passports) = CreateAccess(database);
        _ = Success(await passports.SaveAsync(Save(hostId, "owner-id", "owner"), default));
        var channel = await ResolveAsync(access, "channel");
        var moderatorHost = new BotHostChoice(hostId, "channel", "channel", AuthRole.Moderator);
        var moderator = await ViewerPortalAccess.IdentifyAsync(
            State(
                TestPrincipals.BlokeBotUser(
                    "mod",
                    availableHosts: [moderatorHost],
                    selectedHost: moderatorHost
                )
            )
        );

        var anonymous = await access.OpenPassportAsync(
            channel,
            "@Owner",
            new PortalIdentity.Anonymous(),
            default
        );
        var other = await access.OpenPassportAsync(
            channel,
            "owner",
            Identify("other-id", "other"),
            default
        );
        var asModerator = await access.OpenPassportAsync(channel, "owner", moderator, default);
        var self = await access.OpenPassportAsync(
            channel,
            "owner",
            Identify("owner-id", "owner"),
            default
        );

        _ = anonymous.ShouldBeOfType<PortalPassportOutcome.Hidden>();
        _ = other.ShouldBeOfType<PortalPassportOutcome.Unauthorized>();
        _ = asModerator.ShouldBeOfType<PortalPassportOutcome.Unauthorized>();
        self.ShouldBeOfType<PortalPassportOutcome.Visible>()
            .Passport.TwitchUserId.ShouldBe("owner-id");
        _ = (
            await passports.GetVisibleAsync(
                "channel",
                "owner",
                new ViewerPassportAudience("mod-id", IsChannelManager: true),
                default
            )
        ).ShouldBeOfType<ViewerPassportQueryOutcome.Available>();
    }

    [Test]
    public async Task ErasedViewer_BindingSelfAndOpeningByLogin_ExposesNothingUntilTheyOptBackIn()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var (access, passports) = CreateAccess(database);
        _ = Success(
            await passports.SaveAsync(
                Save(hostId, "owner-id", "gone") with
                {
                    Visibility = ViewerPassportVisibility.Public,
                },
                default
            )
        );
        var channel = await ResolveAsync(access, "channel");
        _ = (
            await access.OpenPassportAsync(channel, "gone", new PortalIdentity.Anonymous(), default)
        ).ShouldBeOfType<PortalPassportOutcome.Visible>();
        await using (var erase = await database.CreateDbContextAsync())
        {
            var report = await ViewerPrivacyService.EraseAsync(
                erase,
                PrivacySubject.Create("owner-id", null),
                hostId,
                default
            );
            report.ChangedRows["viewer-passports.profiles"].ShouldBe(1);
        }

        var erasedSelf = await access.BindSelfAsync(channel, Identify("owner-id", "gone"), default);
        var byLogin = await access.OpenPassportAsync(
            channel,
            "gone",
            new PortalIdentity.Anonymous(),
            default
        );
        var newcomer = await access.BindSelfAsync(channel, Identify("fresh-id", "fresh"), default);
        _ = Success(await passports.SaveAsync(Save(hostId, "owner-id", "gone"), default));
        var optedBackIn = await access.BindSelfAsync(
            channel,
            Identify("owner-id", "gone"),
            default
        );

        _ = erasedSelf.ShouldBeOfType<PortalSelfOutcome.Erased>();
        _ = byLogin.ShouldBeOfType<PortalPassportOutcome.Ambiguous>();
        _ = newcomer.ShouldBeOfType<PortalSelfOutcome.AuthenticatedSelf>();
        _ = optedBackIn.ShouldBeOfType<PortalSelfOutcome.AuthenticatedSelf>();
        _ = (
            await passports.GetVisibleAsync(
                "channel",
                "gone",
                ViewerPassportAudience.Anonymous,
                default
            )
        ).ShouldBeOfType<ViewerPassportQueryOutcome.NotFound>();
    }

    [Test]
    public async Task StaleOrUnavailableSession_Identifying_LeavesPublicDataAvailableAndBindsNothing()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedHostAsync(database, "channel", HostFeatureFlags.Bingo);
        var (access, _) = CreateAccess(database);
        var channel = await ResolveAsync(access, "channel");
        var missingLogin = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "viewer-id")],
                CookieAuthenticationDefaults.AuthenticationScheme
            )
        );
        var undecodableSelection = TestPrincipals.BlokeBotUser(
            "viewer",
            availableHosts: [new BotHostChoice(1, "channel", "channel", AuthRole.Moderator)],
            selectedHostClaim: "not-a-host"
        );

        var stale = await ViewerPortalAccess.IdentifyAsync(State(missingLogin));
        var invalid = await ViewerPortalAccess.IdentifyAsync(State(undecodableSelection));
        var faulted = await ViewerPortalAccess.IdentifyAsync(
            Task.FromException<AuthenticationState>(new InvalidOperationException("cookie"))
        );
        var absent = await ViewerPortalAccess.IdentifyAsync(null);
        var anonymous = await ViewerPortalAccess.IdentifyAsync(State(new ClaimsPrincipal()));

        _ = stale.ShouldBeOfType<PortalIdentity.StaleSession>();
        _ = invalid.ShouldBeOfType<PortalIdentity.StaleSession>();
        _ = faulted.ShouldBeOfType<PortalIdentity.UnavailableAuthentication>();
        _ = absent.ShouldBeOfType<PortalIdentity.UnavailableAuthentication>();
        _ = anonymous.ShouldBeOfType<PortalIdentity.Anonymous>();
        stale.Presentation.ShouldBe(PortalIdentityPresentation.Unavailable);
        faulted.Presentation.ShouldBe(PortalIdentityPresentation.Unavailable);
        _ = (
            await access.BindSelfAsync(channel, stale, default)
        ).ShouldBeOfType<PortalSelfOutcome.StaleSession>();
        _ = (
            await access.BindSelfAsync(channel, faulted, default)
        ).ShouldBeOfType<PortalSelfOutcome.UnavailableAuthentication>();
        _ = (
            await access.BindSelfAsync(channel, anonymous, default)
        ).ShouldBeOfType<PortalSelfOutcome.Anonymous>();
        channel.PublicFeatures.ShouldBe([HostFeatureFlags.Bingo]);
        _ = PortalCacheScope.For(channel.Host, stale).ShouldBeOfType<PortalCacheScope.Private>();
    }

    [Test]
    public async Task TwoHosts_BindingAndOpening_NeverReadAcrossTheHostKey()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstId = await SeedHostAsync(database, "first", HostFeatureFlags.ViewerPassports);
        var secondId = await SeedHostAsync(database, "second", HostFeatureFlags.ViewerPassports);
        var (access, passports) = CreateAccess(database);
        _ = Success(await passports.SaveAsync(Save(firstId, "viewer-id", "old_login"), default));
        _ = Success(await passports.SaveAsync(Save(firstId, "viewer-id", "new_login"), default));
        var first = await ResolveAsync(access, "first");
        var second = await ResolveAsync(access, "second");
        var identity = Identify("viewer-id", "newer_login");

        var onFirst = await access.BindSelfAsync(first, identity, default);
        var onSecond = await access.BindSelfAsync(second, identity, default);
        var historyOnFirst = await access.OpenPassportAsync(
            first,
            "old_login",
            new PortalIdentity.Anonymous(),
            default
        );
        var historyOnSecond = await access.OpenPassportAsync(
            second,
            "old_login",
            new PortalIdentity.Anonymous(),
            default
        );
        await using (var erase = await database.CreateDbContextAsync())
        {
            _ = await ViewerPrivacyService.EraseAsync(
                erase,
                PrivacySubject.Create("viewer-id", null),
                firstId,
                default
            );
        }
        var erasedOnFirst = await access.BindSelfAsync(
            first,
            Identify("viewer-id", "new_login"),
            default
        );
        var untouchedOnSecond = await access.BindSelfAsync(
            second,
            Identify("viewer-id", "new_login"),
            default
        );

        _ = onFirst.ShouldBeOfType<PortalSelfOutcome.Renamed>();
        onSecond
            .ShouldBeOfType<PortalSelfOutcome.AuthenticatedSelf>()
            .Viewer.Host.ShouldBe(second.Host);
        _ = historyOnFirst.ShouldBeOfType<PortalPassportOutcome.HistoricalLogin>();
        _ = historyOnSecond.ShouldBeOfType<PortalPassportOutcome.NotFound>();
        _ = erasedOnFirst.ShouldBeOfType<PortalSelfOutcome.Erased>();
        _ = untouchedOnSecond.ShouldBeOfType<PortalSelfOutcome.AuthenticatedSelf>();
        var firstScope = PortalCacheScope
            .For(first.Host, new PortalIdentity.Anonymous())
            .ShouldBeOfType<PortalCacheScope.Public>();
        var secondScope = PortalCacheScope
            .For(second.Host, new PortalIdentity.Anonymous())
            .ShouldBeOfType<PortalCacheScope.Public>();
        firstScope.Key.ShouldNotBe(secondScope.Key);
    }

    [Test]
    public async Task RecreatedHost_OpeningThroughRetainedChannel_NeverExposesReplacementPassport()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new FixedTimeProvider(_now);
        var changes = new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>());
        var provisioning = new BotHostProvisioningService(database, changes, [], clock);
        var originalId = await provisioning.EnsureHostAsync(
            "channel",
            "host-one",
            "Original host",
            null,
            default
        );
        var (access, passports) = CreateAccess(database);
        var retained = await ResolveAsync(access, "channel");
        var stateDirectory = Directory.CreateTempSubdirectory("blokebot-portal-host-");
        try
        {
            var options = Options.Create(
                new BlokeBotOptions
                {
                    DatabasePath = Path.Combine(stateDirectory.FullName, "blokebot.db"),
                }
            );
            using var maintenance = new OverlayMediaMaintenanceService(
                database,
                options,
                new SystemOverlayMediaFileDeletion(),
                clock,
                NullLogger<OverlayMediaMaintenanceService>.Instance
            );
            var removal = new BotHostRemovalService(
                database,
                changes,
                options,
                maintenance,
                clock,
                NullLogger<BotHostRemovalService>.Instance
            );
            var removed = await removal.RemoveAsync(originalId, default);
            var replacementId = await provisioning.EnsureHostAsync(
                "channel",
                "host-two",
                "Replacement host",
                null,
                default
            );
            await using (var db = await database.CreateDbContextAsync())
            {
                _ = await db
                    .Hosts.Where(host => host.Id == replacementId)
                    .ExecuteUpdateAsync(update =>
                        update.SetProperty(
                            host => host.EnabledFeatures,
                            HostFeatureFlags.ViewerPassports
                        )
                    );
            }
            _ = Success(
                await passports.SaveAsync(
                    Save(replacementId, "viewer-id", "viewer") with
                    {
                        Visibility = ViewerPassportVisibility.Public,
                    },
                    default
                )
            );
            var current = await ResolveAsync(access, "channel");

            var staleResult = await access.OpenPassportAsync(
                retained,
                "viewer",
                new PortalIdentity.Anonymous(),
                default
            );
            var currentResult = await access.OpenPassportAsync(
                current,
                "viewer",
                new PortalIdentity.Anonymous(),
                default
            );

            removed.Removed.ShouldBeTrue();
            replacementId.ShouldNotBe(originalId);
            _ = staleResult.ShouldBeOfType<PortalPassportOutcome.NotFound>();
            current.Host.Id.ShouldBe(replacementId);
            var visible = currentResult.ShouldBeOfType<PortalPassportOutcome.Visible>().Passport;
            visible.HostId.ShouldBe(replacementId);
            visible.TwitchUserId.ShouldBe("viewer-id");
        }
        finally
        {
            stateDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task AnonymousRequest_PublicProjection_IsIdenticalForEveryIdentityAndShareable()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(
            database,
            "channel",
            HostFeatureFlags.ViewerPassports | HostFeatureFlags.Points | HostFeatureFlags.Overlays
        );
        var (access, passports) = CreateAccess(database);
        _ = Success(await passports.SaveAsync(Save(hostId, "viewer-id", "viewer"), default));

        var forAnonymous = await ResolveAsync(access, "channel");
        var forViewer = await ResolveAsync(access, "channel");
        var scope = PortalCacheScope.For(forAnonymous.Host, new PortalIdentity.Anonymous());

        forViewer.Host.ShouldBe(forAnonymous.Host);
        forViewer.DisplayName.ShouldBe(forAnonymous.DisplayName);
        forViewer.PublicFeatures.ShouldBe(forAnonymous.PublicFeatures);
        forAnonymous.PublicFeatures.ShouldBe([
            HostFeatureFlags.Points,
            HostFeatureFlags.ViewerPassports,
        ]);
        var shared = scope.ShouldBeOfType<PortalCacheScope.Public>();
        shared.CacheControl.ShouldBe("public");
        shared.Key.ShouldBe($"viewer-portal:{hostId}:public");
        _ = PortalCacheScope
            .For(forAnonymous.Host, Identify("viewer-id", "viewer"))
            .ShouldBeOfType<PortalCacheScope.Private>();
    }

    private static async Task<PortalChannel> ResolveAsync(
        ViewerPortalAccess access,
        string login
    ) =>
        (await access.ResolveChannelAsync(login, default))
            .ShouldBeOfType<PortalChannelOutcome.Resolved>()
            .Channel;

    private static PortalIdentity Identify(string userId, string login) =>
        ViewerPortalAccess.Identify(AuthenticatedSession.FromPrincipal(Principal(userId, login)));

    private static Task<AuthenticationState> State(ClaimsPrincipal principal) =>
        Task.FromResult(new AuthenticationState(principal));

    private static ClaimsPrincipal Principal(string userId, string login) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, login),
                    new Claim(AuthClaims.Login, login),
                ],
                CookieAuthenticationDefaults.AuthenticationScheme
            )
        );

    private static (ViewerPortalAccess Access, ViewerPassportService Passports) CreateAccess(
        SqliteBlokeBotDbFactory database
    )
    {
        var passports = new ViewerPassportService(
            database,
            new PointBalanceService(database),
            new OfflineStreamLivenessProvider(),
            new FixedTimeProvider(_now)
        );
        return (
            new ViewerPortalAccess(new PublicLeaderboardHostLookup(database), passports, database),
            passports
        );
    }

    private static SaveViewerPassportCommand Save(int hostId, string userId, string login) =>
        new(
            hostId,
            new(userId, login, login),
            "Profile line",
            ViewerPassportVisibility.Private,
            true,
            null,
            null
        );

    private static ViewerPassportView Success(ViewerPassportMutationOutcome outcome) =>
        outcome.ShouldBeOfType<ViewerPassportMutationOutcome.Succeeded>().Passport;

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory database,
        string login,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = features,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class OfflineStreamLivenessProvider : IHostStreamLivenessProvider
    {
        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
            IO<HostStreamLivenessOutcome, Never>.Create(static _ =>
                ValueTask.FromResult(
                    Result<HostStreamLivenessOutcome, Never>.Success(
                        new HostStreamLivenessOutcome.Offline()
                    )
                )
            );
    }
}
