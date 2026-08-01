using System.Net;
using System.Text;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class ModeratorAuthorityServiceTests
{
    [Test]
    public async Task ModeratorWithPriorAuthorization_CheckingAuthority_UsesSessionActorAndAppToken()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Helix.Respond(request =>
        {
            request.RequestUri!.Query.ShouldContain("user_id=moderator-id");
            request.Headers.Authorization!.Parameter.ShouldBe("app-token");
            request.Headers.GetValues("Client-Id").Single().ShouldBe("client-id");
            return JsonResponse("""{"data":[{"broadcaster_login":"streamer"}],"pagination":{}}""");
        });

        var outcome = await fixture.Service.AuthorizeAsync(
            Session(AuthRole.Moderator, fixture.HostId),
            fixture.HostId,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();
        fixture.Tokens.RequestCount.ShouldBe(1);
        fixture.Helix.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task DefinitiveAuthority_CheckingBeforeAndAtExpiry_UsesPerUserHostCacheForFifteenMinutes()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Helix.Respond(_ => AllowedResponse());
        fixture.Helix.Respond(_ => AllowedResponse());
        fixture.Helix.Respond(_ => AllowedResponse());

        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();
        fixture.Time.Advance(TimeSpan.FromMinutes(14).Add(TimeSpan.FromSeconds(59)));
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId, userId: "another-moderator-id"),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();

        fixture.Helix.RequestCount.ShouldBe(2);

        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();

        fixture.Helix.RequestCount.ShouldBe(3);
    }

    [Test]
    public async Task MissingModeratedChannel_CheckingAuthority_ConfirmsRevocationAndCachesTheDenial()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Helix.Respond(_ => JsonResponse("""{"data":[],"pagination":{}}"""));

        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Revoked>();
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Revoked>();

        fixture.Helix.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task ProviderUncertainty_CheckingAuthority_DeniesWithoutCaching()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tokens.Exception = new HttpRequestException("offline");

        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Unavailable>();
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Unavailable>();

        fixture.Tokens.RequestCount.ShouldBe(2);
        fixture.Helix.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task ProviderCancellationNotRequestedByCaller_CheckingAuthority_DeniesWithoutCaching()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tokens.Exception = new OperationCanceledException();

        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Unavailable>();
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Unavailable>();

        fixture.Tokens.RequestCount.ShouldBe(2);
    }

    [Test]
    public async Task CallerCancellation_CheckingAuthority_PropagatesCancellation()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                cancellation.Token
            )
        );
    }

    [Test]
    [Arguments(AuthRole.Streamer)]
    [Arguments(AuthRole.Admin)]
    public async Task BroadcasterAndAdministrator_CheckingAuthority_BypassProvider(AuthRole role)
    {
        await using var fixture = await Fixture.CreateAsync();

        var outcome = await fixture.Service.AuthorizeAsync(
            Session(role, fixture.HostId),
            fixture.HostId,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();
        fixture.Tokens.RequestCount.ShouldBe(0);
        fixture.Helix.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task DifferentRequestedHost_CheckingAuthority_DeniesWithoutProviderOrRevocation()
    {
        await using var fixture = await Fixture.CreateAsync();

        var outcome = await fixture.Service.AuthorizeAsync(
            Session(AuthRole.Moderator, fixture.HostId),
            fixture.HostId + 1,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<ModeratorAuthorityOutcome.HostMismatch>();
        fixture.Tokens.RequestCount.ShouldBe(0);
        fixture.Helix.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task DisabledModeratorConfigurePolicy_CheckingAuthority_RevokesWithoutProvider()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ModeratorAccess.DisableModeratorAccessAsync(
            fixture.HostId,
            CancellationToken.None
        );

        var outcome = await fixture.Service.AuthorizeAsync(
            Session(AuthRole.Moderator, fixture.HostId),
            fixture.HostId,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<ModeratorAuthorityOutcome.Revoked>();
        fixture.Tokens.RequestCount.ShouldBe(0);
        fixture.Helix.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task HostMismatch_ExecutingMutation_DoesNotInvokeCallback()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var services = MutationServices(fixture, out _, out _);
        var component = CreateMutationComponent(services, fixture.HostId);
        var invoked = false;

        await component.MutateAsync(
            fixture.HostId + 1,
            () =>
            {
                invoked = true;
                return Task.CompletedTask;
            }
        );

        invoked.ShouldBeFalse();
    }

    [Test]
    public async Task RevokedModerator_ExecutingMutation_RecoversByClearingModeratorSelection()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Helix.Respond(_ => JsonResponse("""{"data":[],"pagination":{}}"""));
        using var services = MutationServices(fixture, out _, out var navigation);
        var component = CreateMutationComponent(services, fixture.HostId);
        var invoked = false;

        await component.MutateAsync(
            fixture.HostId,
            () =>
            {
                invoked = true;
                return Task.CompletedTask;
            }
        );

        invoked.ShouldBeFalse();
        navigation.LastUri.ShouldNotBeNull();
        navigation.LastUri!.ShouldContain("/auth/recover-moderator-access?hostId=");
        var revoked = new BotHostChoice(fixture.HostId, "streamer", "Streamer", AuthRole.Moderator);
        var remaining = new BotHostChoice(99, "other", "Other", AuthRole.Moderator);
        var recovery = AuthEndpoints.ClearRevokedModeratorHost(
            Session(AuthRole.Moderator, fixture.HostId) with
            {
                AvailableHosts = [revoked, remaining],
            },
            fixture.HostId
        );
        recovery.SelectedHost.ShouldBeNull();
        recovery.Hosts.ShouldBe([remaining]);
    }

    private static AuthenticatedSession Session(
        AuthRole role,
        int hostId,
        string userId = "moderator-id"
    )
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", role);
        return new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = userId,
            Login = "moderator",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private static ServiceProvider MutationServices(
        Fixture fixture,
        out ToastService toasts,
        out RecordingNavigationManager navigation
    )
    {
        toasts = new ToastService();
        navigation = new RecordingNavigationManager();
        return new ServiceCollection()
            .AddSingleton(fixture.Service)
            .AddSingleton(toasts)
            .AddSingleton<NavigationManager>(navigation)
            .BuildServiceProvider();
    }

    private static MutationComponent CreateMutationComponent(IServiceProvider services, int hostId)
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", AuthRole.Moderator);
        var principal = TestPrincipals.BlokeBotUser(
            "moderator",
            role: AuthRole.Moderator,
            availableHosts: [host],
            selectedHost: host
        );
        return new MutationComponent(Task.FromResult(new AuthenticationState(principal)), services);
    }

    private static HttpResponseMessage AllowedResponse() =>
        JsonResponse("""{"data":[{"broadcaster_login":"streamer"}],"pagination":{}}""");

    private static HttpResponseMessage JsonResponse(string json) =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SqliteBlokeBotDbFactory database,
            ManualTimeProvider time,
            FakeAppAccessTokenSource tokens,
            ScriptedHttpClientFactory helix,
            ModeratorAuthorityService service,
            HostModAccessService moderatorAccess,
            int hostId
        )
        {
            Database = database;
            Time = time;
            Tokens = tokens;
            Helix = helix;
            Service = service;
            ModeratorAccess = moderatorAccess;
            HostId = hostId;
        }

        public SqliteBlokeBotDbFactory Database { get; }
        public ManualTimeProvider Time { get; }
        public FakeAppAccessTokenSource Tokens { get; }
        public ScriptedHttpClientFactory Helix { get; }
        public ModeratorAuthorityService Service { get; }
        public HostModAccessService ModeratorAccess { get; }
        public int HostId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            await using (var db = await database.CreateDbContextAsync())
            {
                db.Hosts.Add(
                    new()
                    {
                        Login = "streamer",
                        DisplayName = "Streamer",
                        CreatedAtUtc = DateTime.UtcNow,
                    }
                );
                await db.SaveChangesAsync();
            }

            await using var lookup = await database.CreateDbContextAsync();
            var hostId = lookup.Hosts.Single().Id;
            var events = TestEventBus.Create<AppEventKind>();
            var time = new ManualTimeProvider(
                new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero)
            );
            var tokens = new FakeAppAccessTokenSource();
            var helix = new ScriptedHttpClientFactory();
            var moderatorAccess = new HostModAccessService(
                database,
                new HostedChannelChangeNotifier(events)
            );
            var service = new ModeratorAuthorityService(
                tokens,
                new HelixClient(helix, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
                BotSettings.FromOptions(
                    new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
                ),
                moderatorAccess,
                time
            );
            return new Fixture(database, time, tokens, helix, service, moderatorAccess, hostId);
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class FakeAppAccessTokenSource : IHostBotAppAccessTokenSource
    {
        public Exception? Exception { get; set; }
        public int RequestCount { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            RequestCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Exception is { } exception)
            {
                return Task.FromException<string>(exception);
            }

            return Task.FromResult("app-token");
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now = _now.Add(elapsed);
    }

    private sealed class ScriptedHttpClientFactory : IHttpClientFactory
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public int RequestCount { get; private set; }

        public void Respond(Func<HttpRequestMessage, HttpResponseMessage> response) =>
            _responses.Enqueue(response);

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(ScriptedHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                owner.RequestCount++;
                return Task.FromResult(owner._responses.Dequeue()(request));
            }
        }
    }

    private sealed class MutationComponent : AuthenticatedPageComponent
    {
        public MutationComponent(
            Task<AuthenticationState> authenticationState,
            IServiceProvider services
        )
        {
            AuthenticationState = authenticationState;
            PageContexts = new BlokeBotPageContextAccessor();
            Services = services;
        }

        public Task MutateAsync(int hostId, Func<Task> mutation) =>
            RunSelectedHostMutationAsync(hostId, mutation);
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public RecordingNavigationManager() =>
            Initialize("https://blokebot.test/", "https://blokebot.test/host");

        public string? LastUri { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            LastUri = ToAbsoluteUri(uri).ToString();
    }
}
