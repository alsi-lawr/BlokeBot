using System.Data.Common;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Identity;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostBotAccountAuthorizationTests
{
    private static readonly HostBotAccountActor _owner = new HostBotAccountActor.ChannelOwner(
        "streamer-id",
        "streamer"
    );

    [Test]
    public void ConfiguredBotRedirectUri_CreatingAuthorizationUri_UsesConfiguredValue()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            BotSettings.FromOptions(
                new BotOptions
                {
                    Identity = new BotIdentityOptions
                    {
                        ClientId = "client",
                        RedirectUri = "https://localhost:7107/oauth/callback",
                    },
                }
            ),
            new OAuthTransport(
                httpClientFactory,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            new HelixClient(httpClientFactory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
        );

        var uri = oauth
            .CreateAuthorizationUriForDefaultScopes("state")
            .ShouldBeOfType<OAuthAuthorizationStartOutcome.Ready>()
            .AuthorizationUri;

        uri.Query.ShouldContain("redirect_uri=https%3A%2F%2Flocalhost%3A7107%2Foauth%2Fcallback");
    }

    [Test]
    public void ExplicitScope_CreatingAuthorizationUri_UsesOnlyExplicitSelection()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            BotSettings.FromOptions(
                new BotOptions
                {
                    Identity = new BotIdentityOptions
                    {
                        ClientId = "client",
                        RedirectUri = "https://localhost:7107/oauth/callback",
                        Scopes = ["chat:read"],
                    },
                }
            ),
            new OAuthTransport(
                httpClientFactory,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            new HelixClient(httpClientFactory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
        );

        var uri = oauth
            .CreateAuthorizationUriForScopes(
                "state",
                OAuthAuthorizationScopeSet.Create(["bits:read"])
            )
            .ShouldBeOfType<OAuthAuthorizationStartOutcome.Ready>()
            .AuthorizationUri;

        uri.Query.ShouldContain("scope=bits%3Aread");
        uri.Query.ShouldNotContain("chat%3Aread");
    }

    [Test]
    public void MissingConfiguration_CreatingAuthorizationUri_ReturnsTypedUnavailable()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            BotSettings.FromOptions(new BotOptions()),
            new OAuthTransport(
                httpClientFactory,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            new HelixClient(httpClientFactory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
        );

        var outcome = oauth.CreateAuthorizationUriForDefaultScopes("state");

        _ = outcome.ShouldBeOfType<OAuthAuthorizationStartOutcome.ConfigurationUnavailable>();
    }

    [Test]
    public async Task MissingConfiguration_CompletingAuthorization_ReturnsTypedUnavailable()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            BotSettings.FromOptions(new BotOptions()),
            new OAuthTransport(
                httpClientFactory,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            new HelixClient(httpClientFactory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
        );

        var outcome = await oauth.CompleteAsync("code", CancellationToken.None);

        _ =
            outcome.ShouldBeOfType<OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>.ConfigurationUnavailable>();
    }

    [Test]
    public async Task ProviderRejectedToken_CompletingAuthorization_ReturnsTypedRejection()
    {
        var httpClientFactory = new HostBotAccountHttpClientFactory();
        var oauth = new HostBotAccountOAuthService(
            BotSettings.FromOptions(
                new BotOptions
                {
                    Identity = new BotIdentityOptions
                    {
                        ClientId = "client",
                        ClientSecret = "secret",
                        RedirectUri = "https://localhost:7107/oauth/callback",
                    },
                }
            ),
            new OAuthTransport(
                httpClientFactory,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            new HelixClient(httpClientFactory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
        );

        var outcome = await oauth.CompleteAsync("code", CancellationToken.None);

        _ =
            outcome.ShouldBeOfType<OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>.ProviderNotValidated>();
    }

    [Test]
    public async Task OverrideDisabled_ResolvingBotAccount_ReturnsGlobalAccount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var account = Success(
            await service.GetBotAccount("streamer").ExecuteAsync(CancellationToken.None)
        );

        account.Login.ShouldBe("bot");
        account.AccessToken.ShouldBe("global-token");
    }

    [Test]
    public async Task OverrideDisabled_LoadingStatus_DoesNotReportCustomPermissionGaps()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(
            dbFactory,
            new StaticTokenProvider("global-token"),
            includeFollowRead: true
        );

        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        status.State.ShouldBe(BotAccountAuthorizationState.Disabled);
        status.RequiredScopes.ShouldNotBeEmpty();
        status.GrantedScopes.ShouldBeEmpty();
        status.MissingScopes.ShouldBeEmpty();
    }

    [Test]
    public async Task UnauthorizedOverrideEnabled_ResolvingBotAccount_DoesNotFallbackToGlobal()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);

        var reason = Error(
            await service.GetBotAccount("streamer").ExecuteAsync(CancellationToken.None)
        );

        reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
    }

    [Test]
    public async Task AuthorizedOverrideEnabled_ResolvingBotAccount_ReturnsCustomAccountAndReadyStatus()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);

        var result = await service
            .Authorize(
                hostId,
                _owner,
                new HostBotAccountAuthorizationGrant(
                    new HostBotAccountTokenPayload(
                        "override-token",
                        "override-refresh",
                        DateTimeOffset.UtcNow.AddHours(1)
                    ),
                    "custom-id",
                    LoginName.Parse("custombot"),
                    "CustomBot",
                    "https://static-cdn.jtvnw.net/custombot.png",
                    OAuthScopeSet.Create([
                        "chat:read",
                        "chat:edit",
                        Scopes.UserReadModeratedChannels,
                    ])
                )
            )
            .RunAsync(CancellationToken.None);

        var account = Success(
            await service.GetBotAccount("streamer").ExecuteAsync(CancellationToken.None)
        );
        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        _ = result.ShouldBeOfType<HostBotAccountAuthorizationOutcome.Authorized>();
        account.Login.ShouldBe("custombot");
        account.AccessToken.ShouldBe("override-token");
        status.State.ShouldBe(BotAccountAuthorizationState.Ready);
        status.AuthorizedLogin.ShouldBe("custombot");
        status.AuthorizedProfileImageUrl.ShouldBe("https://static-cdn.jtvnw.net/custombot.png");
    }

    [Test]
    public async Task AuthorizedCustomBot_Persisting_DoesNotStorePlaintextCredentials()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);

        await AuthorizeCustomBotAsync(service, hostId);

        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.HostBotAccountSettings.SingleAsync(x => x.HostId == hostId);
        var protectedPayload = stored.ProtectedTokenPayload.ShouldNotBeNull();
        protectedPayload.AsSpan().IndexOf(Encoding.UTF8.GetBytes("override-token")).ShouldBe(-1);
        protectedPayload.AsSpan().IndexOf(Encoding.UTF8.GetBytes("override-refresh")).ShouldBe(-1);
    }

    [Test]
    public async Task UnprotectFailure_LoadingCustomBot_DisablesCredentialsAndRaisesAlert()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeCustomBotAsync(service, hostId);
        await SetRuntimeStateAsync(dbFactory, hostId, BotChannelRuntimeState.Started);
        await using (var tamper = await dbFactory.CreateDbContextAsync())
        {
            var stored = await tamper.HostBotAccountSettings.SingleAsync();
            var tamperedPayload = stored.ProtectedTokenPayload!.ToArray();
            tamperedPayload[^1] ^= 0x01;
            stored.ProtectedTokenPayload = tamperedPayload;
            _ = await tamper.SaveChangesAsync();
        }

        var reason = Error(
            await service.GetBotAccount("streamer").ExecuteAsync(CancellationToken.None)
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.HostBotAccountSettings.SingleAsync(x => x.HostId == hostId);
        var host = await db.Hosts.SingleAsync(x => x.Id == hostId);
        var alert = await db.DurableAlerts.SingleAsync();
        reason.ShouldBe(AccessTokenUnavailableReason.CredentialProtectionUnavailable);
        settings.OverrideEnabled.ShouldBeFalse();
        settings.ProtectedTokenPayload.ShouldBeNull();
        host.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Stopped);
        alert.Source.ShouldBe(CustomBotCredentialAlert.Source);
        alert.SourceKey.ShouldBe(CustomBotCredentialAlert.SourceKey);
    }

    [Test]
    public async Task RefreshCredentialsRejected_LoadingStatus_ReturnsNotAuthorized()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(
            dbFactory,
            new StaticTokenProvider("global-token"),
            new HostBotAccountHttpClientFactory(HttpStatusCode.BadRequest)
        );
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeExpiredCustomBotAsync(service, hostId);

        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        status.State.ShouldBe(BotAccountAuthorizationState.NotAuthorized);
    }

    [Test]
    [Arguments(HttpStatusCode.TooManyRequests)]
    [Arguments(HttpStatusCode.InternalServerError)]
    public async Task RefreshProviderFailure_LoadingStatus_PropagatesTransportFailure(
        HttpStatusCode statusCode
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(
            dbFactory,
            new StaticTokenProvider("global-token"),
            new HostBotAccountHttpClientFactory(statusCode)
        );
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeExpiredCustomBotAsync(service, hostId);

        var exception = await Should.ThrowAsync<HttpRequestException>(() =>
            service.GetStatusAsync(hostId, CancellationToken.None)
        );

        exception.StatusCode.ShouldBe(statusCode);
    }

    [Test]
    public async Task DifferentChannels_ResolvingConcurrently_DoesNotRetainAccountAcrossChannels()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var customHostId = await SeedHostAsync(dbFactory, "custom-channel");
        _ = await SeedHostAsync(dbFactory, "global-channel");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(customHostId, CancellationToken.None);
        await AuthorizeCustomBotAsync(
            service,
            customHostId,
            new HostBotAccountActor.ChannelOwner("custom-owner-id", "custom-channel")
        );

        var lookups = Enumerable
            .Range(0, 8)
            .SelectMany(_ =>
                new[] { ResolveAsync("custom-channel"), ResolveAsync("global-channel") }
            )
            .ToArray();

        var results = await Task.WhenAll(lookups);

        foreach (var result in results)
        {
            var expected =
                result.Channel == "custom-channel"
                    ? new BotAccount("custombot", "override-token")
                    : new BotAccount("bot", "global-token");
            result.Account.ShouldBe(expected);
        }

        async Task<(string Channel, BotAccount Account)> ResolveAsync(string channel) =>
            (
                channel,
                Success(await service.GetBotAccount(channel).ExecuteAsync(CancellationToken.None))
            );
    }

    [Test]
    public async Task OverrideDisabled_EnablingWhispers_RejectsWithoutPersisting()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var outcome = await service.EnableWhisperResponsesAsync(hostId, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        _ = outcome.ShouldBeOfType<WhisperResponseConfigurationOutcome.CustomBotRequired>();
        (
            await db.HostBotAccountSettings.SingleOrDefaultAsync(
                x => x.HostId == hostId,
                CancellationToken.None
            )
        ).ShouldBeNull();
    }

    [Test]
    public async Task MissingHost_ConfiguringWhispers_ReturnsTypedMissingOutcome()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));

        var enabling = await service.EnableWhisperResponsesAsync(42, CancellationToken.None);
        var disabling = await service.DisableWhisperResponsesAsync(42, CancellationToken.None);

        _ = enabling.ShouldBeOfType<WhisperResponseConfigurationOutcome.HostNotFound>();
        _ = disabling.ShouldBeOfType<WhisperResponseConfigurationOutcome.HostNotFound>();
    }

    [Test]
    public async Task WhispersEnabled_DisablingWhispers_PersistsPublicChatConfiguration()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        _ = (
            await service.EnableWhisperResponsesAsync(hostId, CancellationToken.None)
        ).ShouldBeOfType<WhisperResponseConfigurationOutcome.Configured>();

        var outcome = await service.DisableWhisperResponsesAsync(hostId, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        _ = outcome.ShouldBeOfType<WhisperResponseConfigurationOutcome.Configured>();
        (
            await db.HostBotAccountSettings.SingleAsync(
                x => x.HostId == hostId,
                CancellationToken.None
            )
        ).WhisperResponsesEnabled.ShouldBeFalse();
    }

    [Test]
    public async Task WhispersEnabled_AuthorizingCustomBot_RequiresWhisperScope()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        _ = (
            await service.EnableWhisperResponsesAsync(hostId, CancellationToken.None)
        ).ShouldBeOfType<WhisperResponseConfigurationOutcome.Configured>();

        var missing = await service
            .Authorize(
                hostId,
                _owner,
                CreateCustomBotGrant(
                    "override-token",
                    [
                        "chat:read",
                        "chat:edit",
                        Scopes.UserReadModeratedChannels,
                        Scopes.UserReadFollows,
                    ]
                )
            )
            .RunAsync(CancellationToken.None);
        var authorized = await service
            .Authorize(
                hostId,
                _owner,
                CreateCustomBotGrant(
                    "override-whisper-token",
                    [
                        "chat:read",
                        "chat:edit",
                        Scopes.UserReadModeratedChannels,
                        Scopes.UserReadFollows,
                        Scopes.UserManageWhispers,
                    ]
                )
            )
            .RunAsync(CancellationToken.None);
        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        missing
            .ShouldBeOfType<HostBotAccountAuthorizationOutcome.MissingScopes>()
            .Scopes.ShouldContain(Scopes.UserManageWhispers);
        _ = authorized.ShouldBeOfType<HostBotAccountAuthorizationOutcome.Authorized>();
        status.State.ShouldBe(BotAccountAuthorizationState.Ready);
        status.RequiredScopes.ShouldContain(Scopes.UserManageWhispers);
    }

    [Test]
    public async Task MissingFollowReadScope_AuthorizingCustomBot_RequiresReconnect()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(
            dbFactory,
            new StaticTokenProvider("global-token"),
            includeFollowRead: true
        );
        await service.UseCustomBotAsync(hostId, CancellationToken.None);

        var outcome = await service
            .Authorize(
                hostId,
                _owner,
                CreateCustomBotGrant(
                    "override-token",
                    ["chat:read", "chat:edit", Scopes.UserReadModeratedChannels]
                )
            )
            .RunAsync(CancellationToken.None);

        outcome
            .ShouldBeOfType<HostBotAccountAuthorizationOutcome.MissingScopes>()
            .Scopes.ShouldBe([Scopes.UserReadFollows]);
    }

    [Test]
    public async Task MissingAnnouncementManagementScope_AuthorizingCustomBot_RequiresReconnect()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(
            dbFactory,
            new StaticTokenProvider("global-token"),
            includeFollowRead: true,
            includeAnnouncementManagement: true
        );
        await service.UseCustomBotAsync(hostId, CancellationToken.None);

        var outcome = await service
            .Authorize(
                hostId,
                _owner,
                CreateCustomBotGrant(
                    "override-token",
                    [
                        "chat:read",
                        "chat:edit",
                        Scopes.UserReadModeratedChannels,
                        Scopes.UserReadFollows,
                    ]
                )
            )
            .RunAsync(CancellationToken.None);

        outcome
            .ShouldBeOfType<HostBotAccountAuthorizationOutcome.MissingScopes>()
            .Scopes.ShouldBe([Scopes.ModeratorManageAnnouncements]);
    }

    [Test]
    public async Task BotAdministrator_AuthorizingThenClearingCustomBot_SucceedsForManagedHost()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        var administrator = new HostBotAccountActor.BotAdministrator("admin-id", "administrator");
        await service.UseCustomBotAsync(hostId, CancellationToken.None);

        var canAuthorize = await service.CanAuthorizeAsync(
            hostId,
            administrator,
            CancellationToken.None
        );
        var authorization = await service
            .Authorize(
                hostId,
                administrator,
                CreateCustomBotGrant(
                    "override-token",
                    ["chat:read", "chat:edit", Scopes.UserReadModeratedChannels]
                )
            )
            .RunAsync(CancellationToken.None);
        var clearing = await service.ClearAsync(hostId, administrator, CancellationToken.None);
        var status = await service.GetStatusAsync(hostId, CancellationToken.None);

        canAuthorize.ShouldBeTrue();
        _ = authorization.ShouldBeOfType<HostBotAccountAuthorizationOutcome.Authorized>();
        _ = clearing.ShouldBeOfType<HostBotAccountClearOutcome.Cleared>();
        status.State.ShouldBe(BotAccountAuthorizationState.NotAuthorized);
    }

    [Test]
    public async Task RunningHostWithOverride_DisablingOverride_QueuesRestartWithGlobalAccount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeCustomBotAsync(service, hostId);
        await SetRuntimeStateAsync(dbFactory, hostId, BotChannelRuntimeState.Started);

        await service.UseMainBotAsync(hostId, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.FindAsync([hostId], CancellationToken.None);
        var settings = await db.HostBotAccountSettings.SingleAsync(
            x => x.HostId == hostId,
            CancellationToken.None
        );
        host!.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Starting);
        settings.OverrideEnabled.ShouldBeFalse();
    }

    [Test]
    public async Task RunningHostWithAuthorizedOverride_EnablingOverride_QueuesRestartWithCustomAccount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, new StaticTokenProvider("global-token"));
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeCustomBotAsync(service, hostId);
        await service.UseMainBotAsync(hostId, CancellationToken.None);
        await SetRuntimeStateAsync(dbFactory, hostId, BotChannelRuntimeState.Started);

        await service.UseCustomBotAsync(hostId, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.FindAsync([hostId], CancellationToken.None);
        var settings = await db.HostBotAccountSettings.SingleAsync(
            x => x.HostId == hostId,
            CancellationToken.None
        );
        host!.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Starting);
        settings.OverrideEnabled.ShouldBeTrue();
    }

    [Test]
    public async Task AccountSelectionCommitCanceled_RollsBackAccountRuntimeAndSessionIdentity()
    {
        var commitCancellation = new CommitCancellationInterceptor();
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync(commitCancellation);
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var changes = new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>());
        var transitions = new HostedChannelRuntimeTransitionService(dbFactory, changes);
        var lifecycle = new HostedChannelRuntimeLifecycleService(transitions);
        var service = CreateService(
            dbFactory,
            new StaticTokenProvider("global-token"),
            runtimeTransitions: transitions
        );
        await service.UseCustomBotAsync(hostId, CancellationToken.None);
        await AuthorizeCustomBotAsync(service, hostId);
        await SetRuntimeStateAsync(dbFactory, hostId, BotChannelRuntimeState.Started);
        var priorTarget = await transitions.GetOrCreateSessionTargetAsync(
            hostId,
            "streamer",
            CancellationToken.None
        );
        commitCancellation.CancelNextCommit();

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            service.UseMainBotAsync(hostId, CancellationToken.None)
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.AsNoTracking().SingleAsync(x => x.Id == hostId);
        var settings = await db
            .HostBotAccountSettings.AsNoTracking()
            .SingleAsync(x => x.HostId == hostId);
        var currentTarget = await transitions.GetOrCreateSessionTargetAsync(
            hostId,
            "streamer",
            CancellationToken.None
        );
        commitCancellation.CommitAttempts.ShouldBe(1);
        settings.OverrideEnabled.ShouldBeTrue();
        host.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Started);
        ReferenceEquals(priorTarget.SessionIdentity, currentTarget.SessionIdentity).ShouldBeTrue();
        (await lifecycle.MarkStartedAsync(priorTarget, CancellationToken.None)).ShouldBeTrue();
    }

    private static HostBotAccountAuthorizationService CreateService(
        SqliteBlokeBotDbFactory dbFactory,
        IAccessTokenProvider? tokenProvider,
        HostBotAccountHttpClientFactory? httpClientFactory = null,
        bool includeFollowRead = false,
        bool includeAnnouncementManagement = false,
        HostedChannelRuntimeTransitionService? runtimeTransitions = null
    )
    {
        httpClientFactory ??= new HostBotAccountHttpClientFactory();
        var options = BotSettings.FromOptions(
            new BotOptions
            {
                Identity = new BotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost:7107/oauth/callback",
                    Scopes =
                    [
                        "chat:read",
                        "chat:edit",
                        Scopes.UserReadModeratedChannels,
                        .. includeFollowRead ? [Scopes.UserReadFollows] : Array.Empty<string>(),
                        .. includeAnnouncementManagement
                            ? [Scopes.ModeratorManageAnnouncements]
                            : Array.Empty<string>(),
                    ],
                },
            }
        );
        var oauth = new OAuthTransport(
            httpClientFactory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );
        var helix = new HelixClient(
            httpClientFactory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );
        var changes = new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>());
        return new HostBotAccountAuthorizationService(
            dbFactory,
            new HostBotAccountOAuthService(options, oauth, helix),
            oauth,
            helix,
            HostBotAccountTokenProtectionTestSupport.CreateProtector(),
            tokenProvider is null
                ? new UnavailableTokenStatusSource()
                : new TokenStatusService(
                    tokenProvider,
                    oauth,
                    NullLogger<TokenStatusService>.Instance
                ),
            changes,
            options,
            runtimeTransitions ?? new HostedChannelRuntimeTransitionService(dbFactory, changes)
        );
    }

    private static BotAccount Success(Result<BotAccount, AccessTokenUnavailableReason> result) =>
        result.Match(
            static account => account,
            static reason =>
                throw new InvalidOperationException(
                    $"Expected an authorized bot account, received {reason}."
                )
        );

    private static AccessTokenUnavailableReason Error(
        Result<BotAccount, AccessTokenUnavailableReason> result
    ) =>
        result.Match(
            static _ => throw new InvalidOperationException("Expected token unavailability."),
            static reason => reason
        );

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
            DisplayName = login,
            Login = login,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task AuthorizeCustomBotAsync(
        HostBotAccountAuthorizationService service,
        int hostId,
        HostBotAccountActor? actor = null
    )
    {
        var result = await service
            .Authorize(
                hostId,
                actor ?? _owner,
                CreateCustomBotGrant(
                    "override-token",
                    [
                        "chat:read",
                        "chat:edit",
                        Scopes.UserReadModeratedChannels,
                        Scopes.UserReadFollows,
                    ]
                )
            )
            .RunAsync(CancellationToken.None);

        _ = result.ShouldBeOfType<HostBotAccountAuthorizationOutcome.Authorized>();
    }

    private static async Task AuthorizeExpiredCustomBotAsync(
        HostBotAccountAuthorizationService service,
        int hostId
    )
    {
        var result = await service
            .Authorize(
                hostId,
                _owner,
                CreateCustomBotGrant(
                    "expired-token",
                    [
                        "chat:read",
                        "chat:edit",
                        Scopes.UserReadModeratedChannels,
                        Scopes.UserReadFollows,
                    ],
                    DateTimeOffset.UtcNow.AddMinutes(-1)
                )
            )
            .RunAsync(CancellationToken.None);

        _ = result.ShouldBeOfType<HostBotAccountAuthorizationOutcome.Authorized>();
    }

    private static HostBotAccountAuthorizationGrant CreateCustomBotGrant(
        string accessToken,
        IReadOnlyList<string> scopes,
        DateTimeOffset? expiresAtUtc = null
    ) =>
        new(
            new HostBotAccountTokenPayload(
                accessToken,
                "override-refresh",
                expiresAtUtc ?? DateTimeOffset.UtcNow.AddHours(1)
            ),
            "custom-id",
            LoginName.Parse("custombot"),
            "CustomBot",
            "https://static-cdn.jtvnw.net/custombot.png",
            OAuthScopeSet.Create(scopes)
        );

    private static async Task SetRuntimeStateAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        BotChannelRuntimeState state
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.FindAsync([hostId], CancellationToken.None);
        host!.BotRuntimeState = state;
        host.BotRuntimeStateChangedAtUtc = DateTime.UtcNow;
        _ = await db.SaveChangesAsync();
    }

    private sealed class StaticTokenProvider(string accessToken) : IAccessTokenProvider
    {
        public IO<string, AccessTokenUnavailableReason> GetAccessToken() =>
            IO<string, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<string, AccessTokenUnavailableReason>.Success(accessToken)
                )
            );
    }

    private sealed class CommitCancellationInterceptor : DbTransactionInterceptor
    {
        private bool _cancelNextCommit;

        internal int CommitAttempts { get; private set; }

        internal void CancelNextCommit() => _cancelNextCommit = true;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (!_cancelNextCommit)
            {
                return ValueTask.FromResult(result);
            }

            _cancelNextCommit = false;
            CommitAttempts++;
            return ValueTask.FromException<InterceptionResult>(
                new OperationCanceledException("commit cancellation")
            );
        }
    }

    private sealed class HostBotAccountHttpClientFactory(HttpStatusCode? tokenStatusCode = null)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(tokenStatusCode));

        private sealed class Handler(HttpStatusCode? tokenStatusCode) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) =>
                Task.FromResult(
                    request.RequestUri?.AbsolutePath switch
                    {
                        "/oauth2/token" => tokenStatusCode is { } statusCode
                            ? new HttpResponseMessage(statusCode)
                            : JsonResponse(
                                """
                                {"access_token":"grant-token","refresh_token":"refresh","expires_in":3600}
                                """
                            ),
                        "/oauth2/validate" => ValidationResponse(request),
                        "/helix/users" => JsonResponse(
                            """
                            {"data":[{"id":"custom-id","login":"custombot","display_name":"CustomBot","profile_image_url":"https://static-cdn.jtvnw.net/custombot.png"}]}
                            """
                        ),
                        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                    }
                );

            private static HttpResponseMessage ValidationResponse(HttpRequestMessage request) =>
                request.Headers.Authorization?.Parameter switch
                {
                    "global-token" => JsonResponse(
                        """
                        {"user_id":"bot-id","login":"bot","scopes":["chat:read","chat:edit","user:read:moderated_channels"]}
                        """
                    ),
                    "override-token" => JsonResponse(
                        """
                        {"user_id":"custom-id","login":"custombot","scopes":["chat:read","chat:edit","user:read:moderated_channels"]}
                        """
                    ),
                    "override-whisper-token" => JsonResponse(
                        """
                        {"user_id":"custom-id","login":"custombot","scopes":["chat:read","chat:edit","user:read:moderated_channels","user:manage:whispers"]}
                        """
                    ),
                    _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                };

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }
}
