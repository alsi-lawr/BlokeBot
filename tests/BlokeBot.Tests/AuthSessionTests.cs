using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Eventing;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class AuthSessionTests
{
    [Test]
    public void BotAccountPrincipal_CreatingSession_GrantsAdminButNotOperatorCapability()
    {
        var session = AuthenticatedSession.FromPrincipal(
            TestPrincipals.BlokeBotUser(
                login: "botaccount",
                role: AuthRole.Bot,
                isBotAdmin: true,
                isBotAccount: true
            )
        );

        session.IsAuthenticated.ShouldBeTrue();
        session.IsBotAccount.ShouldBeTrue();
        session.IsBotAdmin.ShouldBeTrue();
        session.HasCapability(AuthSessionCapability.BotAdmin).ShouldBeTrue();
        session.HasCapability(AuthSessionCapability.Operator).ShouldBeFalse();
        session.HostSelection.ShouldBeNull();
    }

    [Test]
    public void InvalidSelectedHostClaim_CreatingSession_DoesNotFallbackToAvailableHost()
    {
        var available = new[]
        {
            new BotHostChoice(1, "streamer", "Streamer", AuthRole.Streamer),
            new BotHostChoice(2, "other", "Other", AuthRole.Moderator),
        };

        var session = AuthenticatedSession.FromPrincipal(
            TestPrincipals.BlokeBotUser(
                login: "streamer",
                role: AuthRole.Streamer,
                availableHosts: available,
                selectedHostClaim: "not-json"
            )
        );

        session.HostSelectionState.ShouldBe(AuthSessionHostSelectionState.Invalid);
        session.HostSelection.ShouldBeNull();
        session.HasCapability(AuthSessionCapability.Operator).ShouldBeFalse();
        session.HasCapability(AuthSessionCapability.HostSelected).ShouldBeFalse();
    }

    [Test]
    public void MalformedRoleClaim_CreatingSession_MarksClaimsInvalid()
    {
        var session = AuthenticatedSession.FromPrincipal(
            TestPrincipals.BlokeBotUser(login: "streamer", roleClaim: "owner")
        );

        session.ClaimsValid.ShouldBeFalse();
        session.Role.ShouldBeNull();
    }

    [Test]
    public void MalformedAvailableHostClaim_CreatingSession_MarksClaimsInvalid()
    {
        var session = AuthenticatedSession.FromPrincipal(
            TestPrincipals.BlokeBotUser(
                login: "streamer",
                role: AuthRole.Streamer,
                availableHostClaims: ["1|streamer|Streamer|streamer|"]
            )
        );

        session.ClaimsValid.ShouldBeFalse();
        session.HostSelection.ShouldBeNull();
    }

    [Test]
    public void SelectedHostConflictsWithAvailableHost_CreatingSession_MarksSelectionInvalid()
    {
        var available = new BotHostChoice(1, "streamer", "Streamer", AuthRole.Streamer);
        var selected = available with { Role = AuthRole.Moderator };

        var session = AuthenticatedSession.FromPrincipal(
            TestPrincipals.BlokeBotUser(
                login: "streamer",
                role: AuthRole.Streamer,
                availableHosts: [available],
                selectedHost: selected
            )
        );

        session.HostSelectionState.ShouldBe(AuthSessionHostSelectionState.Invalid);
        session.HostSelection.ShouldBeNull();
    }

    [Test]
    public void StructuredHostChoice_EncodingThenDecoding_RoundTrips()
    {
        var host = new BotHostChoice(
            42,
            "streamer",
            "Streamer",
            AuthRole.Streamer,
            "https://example.test/profile.png"
        );

        var decoded = BotHostClaimCodec.Decode(BotHostClaimCodec.Encode(host));

        decoded.ShouldBe(host);
    }

    [Test]
    public void LegacyPipeHostPayload_Decoding_ReturnsNull()
    {
        BotHostClaimCodec.Decode("42|streamer|Streamer|streamer|").ShouldBeNull();
    }

    [Test]
    public void CreateAllowedUserWithModeratorSelection_CheckingHostConfigAccess_ReturnsTrue()
    {
        var selectedHost = new BotHostChoice(7, "managed", "Managed", AuthRole.Moderator);
        var session = AuthenticatedSession.FromPrincipal(
            TestPrincipals.BlokeBotUser(
                login: "streamer",
                role: AuthRole.Moderator,
                canCreateHost: true,
                availableHosts: [selectedHost],
                selectedHost: selectedHost
            )
        );

        session.CanOpenHostConfig(new HashSet<int> { selectedHost.Id }).ShouldBeTrue();
    }

    [Test]
    public void CreateAllowedUserWithoutSelection_CreatingSession_PreservesHostsWithoutBotAccess()
    {
        var alternateHost = new BotHostChoice(7, "managed", "Managed", AuthRole.Moderator);
        var session = AuthenticatedSession.FromPrincipal(
            TestPrincipals.BlokeBotUser(
                login: "streamer",
                canCreateHost: true,
                availableHosts: [alternateHost]
            )
        );

        session.HostSelectionState.ShouldBe(AuthSessionHostSelectionState.None);
        session.HostSelection.ShouldBeNull();
        session.AvailableHosts.Single().ShouldBe(alternateHost);
        session.CanOpenHostConfig(new HashSet<int> { alternateHost.Id }).ShouldBeTrue();
        session.CanUseBotFunctions(new HashSet<int> { alternateHost.Id }).ShouldBeFalse();
    }

    [Test]
    public async Task CreateAllowedUserWithoutSelection_ValidatingCookie_RemainsAuthenticated()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var validator = CreateValidator(dbFactory);
        var context = CookieContext(
            TestPrincipals.BlokeBotUser(
                login: "streamer",
                canCreateHost: true,
                availableHosts: [new BotHostChoice(7, "managed", "Managed", AuthRole.Moderator)]
            )
        );

        await validator.ValidateAsync(context);

        context.Principal.ShouldNotBeNull();
    }

    [Test]
    public async Task InvalidSelectedHostClaim_ValidatingCookie_RejectsPrincipalWithoutFallback()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(dbFactory, "streamer");
        var validator = CreateValidator(dbFactory);
        var context = CookieContext(
            TestPrincipals.BlokeBotUser(
                login: "streamer",
                role: AuthRole.Streamer,
                canCreateHost: false,
                availableHosts: [new BotHostChoice(1, "streamer", "Streamer", AuthRole.Streamer)],
                selectedHostClaim: "not-json"
            )
        );

        await validator.ValidateAsync(context);

        context.Principal.ShouldBeNull();
    }

    [Test]
    public async Task RevokedModeratorAccess_ValidatingCookie_RejectsPrincipal()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var modAccess = new HostModAccessService(
            dbFactory,
            new HostedChannelChangeNotifier(new EventBus<AppEventKind>())
        );
        await modAccess.AddEntryAsync(
            hostId,
            AccessListEntryKind.Blacklist,
            "moderator",
            CancellationToken.None
        );
        var validator = CreateValidator(dbFactory, modAccess);
        var selectedHost = new BotHostChoice(hostId, "streamer", "Streamer", AuthRole.Moderator);
        var context = CookieContext(
            TestPrincipals.BlokeBotUser(
                login: "moderator",
                role: AuthRole.Moderator,
                availableHosts: [selectedHost],
                selectedHost: selectedHost
            )
        );

        await validator.ValidateAsync(context);

        context.Principal.ShouldBeNull();
    }

    private static AuthCookieValidator CreateValidator(
        SqliteBlokeBotDbFactory dbFactory,
        HostModAccessService? modAccess = null,
        string[]? botAdmins = null,
        string botUsername = "botaccount"
    )
    {
        var appEvents = new EventBus<AppEventKind>();
        var admins = new BotAdminService(
            Options.Create(new BlokeBotOptions { BotAdmins = botAdmins ?? [] })
        );
        return new AuthCookieValidator(
            dbFactory,
            modAccess
                ?? new HostModAccessService(dbFactory, new HostedChannelChangeNotifier(appEvents)),
            new SiteAccessService(dbFactory, admins, new SiteAccessChangeNotifier(appEvents)),
            admins,
            new AuthSessionService(
                admins,
                Options.Create(
                    new TwitchBotOptions
                    {
                        Identity = new TwitchBotIdentityOptions { BotUsername = botUsername },
                    }
                )
            )
        );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static CookieValidatePrincipalContext CookieContext(ClaimsPrincipal principal)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie()
            .Services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CookieAuthenticationDefaults.AuthenticationScheme,
            typeof(CookieAuthenticationHandler)
        );
        var ticket = new AuthenticationTicket(
            principal,
            CookieAuthenticationDefaults.AuthenticationScheme
        );
        return new CookieValidatePrincipalContext(
            httpContext,
            scheme,
            new CookieAuthenticationOptions(),
            ticket
        );
    }
}
