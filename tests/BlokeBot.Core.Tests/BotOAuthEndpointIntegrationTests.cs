using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.BotRuntime;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class BotOAuthEndpointIntegrationTests
{
    private static readonly Uri _authorizationUri = new(
        "https://id.twitch.tv/oauth2/authorize?state=test"
    );

    [Test]
    public async Task ConfiguredBotOAuth_AuthenticatedBotAdminStarting_RedirectsToAuthorization()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldBe(_authorizationUri);
    }

    [Test]
    public async Task UnavailableBotOAuth_AuthenticatedBotAdminStarting_ReturnsActionableResult()
    {
        await using var host = await EndpointHost.StartAsync(configured: false);

        using var response = await host.Client.GetAsync("/oauth/start");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Twitch connection unavailable");
        page.ShouldContain("Return to Admin");
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task ReplayedGlobalOAuthState_AuthenticatedBotAdminCompleting_ReturnsExpiredState()
    {
        var flow = new StubOAuthFlow(_authorizationUri)
        {
            CompletionOutcome = new OAuthFlowCompletionOutcome.InvalidState(),
        };
        await using var host = await EndpointHost.StartAsync(configured: true, flow);

        using var response = await host.Client.GetAsync("/oauth/callback?code=code&state=replayed");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Connection expired");
        page.ShouldContain("That bot-account connection has expired.");
        page.ShouldContain("No changes were made.");
        page.ShouldContain("A BlokeBot administrator can start a new connection.");
        page.ShouldContain("Try again");
        page.ShouldContain("Return to Admin");
        page.ShouldContain("Close window");
        page.ShouldNotContain("Channel setup");
        page.ShouldNotContain("channel owner");
    }

    [Test]
    public async Task CancelledGlobalOAuth_AccessDenied_RedactsProviderMessage()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/callback?error=access_denied");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Connection cancelled");
        page.ShouldContain("Twitch did not connect the BlokeBot bot account.");
        page.ShouldContain("A BlokeBot administrator can try again when they are ready.");
        page.ShouldContain("Return to Admin");
        page.ShouldNotContain("access_denied");
        page.ShouldNotContain("Channel setup");
        page.ShouldNotContain("channel owner");
    }

    [Test]
    public async Task GlobalOAuth_UnexpectedProviderError_ReturnsTemporaryFailureWithSupportReference()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/callback?error=provider-secret");

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Twitch is temporarily unavailable");
        page.ShouldContain("BlokeBot could not finish connecting the bot account right now.");
        page.ShouldContain("A BlokeBot administrator can try again in a few minutes.");
        page.ShouldContain("Support reference:");
        page.ShouldContain("Get help");
        page.ShouldContain("Return to Admin");
        page.ShouldNotContain("provider-secret");
        page.ShouldNotContain("Channel setup");
        page.ShouldNotContain("channel owner");
    }

    [Test]
    public async Task GlobalOAuth_Completed_ReturnsBotAccountSpecificSuccess()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/callback?code=code&state=state");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Bot account connected");
        page.ShouldContain("BlokeBot has saved Twitch access for the bot account.");
        page.ShouldContain("The bot account connection has been updated.");
        page.ShouldContain("Return to Admin");
        page.ShouldNotContain("Channel setup");
        page.ShouldNotContain("channel owner");
    }

    [Test]
    public async Task ConfiguredBotOAuth_AuthenticatedNonAdminStarting_ReturnsAdministratorGuidance()
    {
        await using var host = await EndpointHost.StartAsync(configured: true, isBotAdmin: false);

        using var response = await host.Client.GetAsync("/oauth/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Only a BlokeBot administrator can open this page.");
        page.ShouldContain("Return to Admin");
    }

    [Test]
    public async Task ChannelOAuth_NoSelectedChannel_ReturnsExactChannelGuidance()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/channel-bot/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Choose a channel to continue");
    }

    [Test]
    public async Task ChannelOAuth_SelectedNonOwnerStarting_ReturnsOperatorAccessGuidance()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Moderator,
            login: "moderator"
        );

        using var response = await host.Client.GetAsync("/oauth/channel-bot/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain(
            "The channel owner or server administrator must grant you access before you can reconnect the bot."
        );
        page.ShouldNotContain("Channel owner needs to reconnect the bot.");
    }

    [Test]
    public async Task ChannelOAuth_WrongAccountCompleting_IdentifiesRequiredChannelAccount()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.ChannelWrongAccount
        );

        using var request = CallbackRequest(
            "/oauth/channel-bot/callback?code=code&state=state",
            "BlokeBot.ChannelBotState"
        );
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("@streamer is the Twitch account needed for this channel.");
        page.ShouldContain("The channel owner needs to reconnect the bot using that account.");
        page.ShouldContain("Try again");
        page.ShouldContain("Return to Channel setup");
    }

    [Test]
    public async Task ChannelOAuth_MissingPermissionCompleting_ReturnsPermissionGuidance()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.ChannelMissingPermission
        );

        using var request = CallbackRequest(
            "/oauth/channel-bot/callback?code=code&state=state",
            "BlokeBot.ChannelBotState"
        );
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("More Twitch access is needed");
        page.ShouldContain("Try again and approve every permission Twitch shows.");
        page.ShouldContain("Return to Channel setup");
    }

    [Test]
    public async Task ChannelOAuth_UnexpectedProviderError_ReturnsTemporaryFailureWithoutRawError()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer"
        );

        using var request = CallbackRequest(
            "/oauth/channel-bot/callback?error=provider-secret",
            "BlokeBot.ChannelBotState"
        );
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Twitch is temporarily unavailable");
        page.ShouldContain("Support reference:");
        page.ShouldContain("Return to Channel setup");
        page.ShouldNotContain("provider-secret");
    }

    [Test]
    public async Task HostBotOAuth_MissingPermissionCompleting_ReturnsPermissionGuidance()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.HostMissingPermission
        );

        using var request = CallbackRequest(
            "/oauth/callback?code=code&state=state",
            "BlokeBot.HostBotState"
        );
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("More Twitch access is needed");
        page.ShouldContain("Try again and approve every permission Twitch shows.");
        page.ShouldContain("Return to Channel setup");
    }

    [Test]
    public async Task HostBotOAuth_UnexpectedProviderError_ReturnsTemporaryFailureWithoutRawError()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer"
        );

        using var request = CallbackRequest(
            "/oauth/callback?error=provider-secret",
            "BlokeBot.HostBotState"
        );
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Twitch is temporarily unavailable");
        page.ShouldContain("Support reference:");
        page.ShouldContain("Return to Channel setup");
        page.ShouldNotContain("provider-secret");
    }

    [Test]
    public async Task ConnectionResultPages_RenderListedOutcomesWithAppropriateActions()
    {
        await AssertResultPageAsync(
            TwitchConnectionResultPage.Cancelled("/oauth/channel-bot/start"),
            HttpStatusCode.BadRequest,
            "Connection cancelled",
            "Try again"
        );
        await AssertResultPageAsync(
            TwitchConnectionResultPage.Expired("/oauth/channel-bot/start"),
            HttpStatusCode.BadRequest,
            "Connection expired",
            "Try again"
        );
        await AssertResultPageAsync(
            TwitchConnectionResultPage.WrongChannelAccount("streamer", "/oauth/channel-bot/start"),
            HttpStatusCode.BadRequest,
            "@streamer is the Twitch account needed for this channel.",
            "Try again"
        );
        await AssertResultPageAsync(
            TwitchConnectionResultPage.PermissionNeeded("/oauth/channel-bot/start"),
            HttpStatusCode.BadRequest,
            "More Twitch access is needed",
            "Try again and approve every permission Twitch shows."
        );
        await AssertResultPageAsync(
            TwitchConnectionResultPage.ProviderTemporarilyUnavailable(
                "/oauth/channel-bot/start",
                "request<&"
            ),
            HttpStatusCode.BadGateway,
            "Twitch is temporarily unavailable",
            "Support reference: <code>request&lt;&amp;</code>"
        );
        await AssertResultPageAsync(
            TwitchConnectionResultPage.NoChannelSelected(),
            HttpStatusCode.Forbidden,
            "Choose a channel to continue",
            "Return to Channel setup"
        );
        await AssertResultPageAsync(
            TwitchConnectionResultPage.OperatorAccessRequired(),
            HttpStatusCode.Forbidden,
            "The channel owner or server administrator must grant you access before you can reconnect the bot.",
            "Return to Channel setup"
        );
    }

    private static async Task AssertResultPageAsync(
        IResult result,
        HttpStatusCode expectedStatus,
        string expectedCopy,
        string expectedAction
    )
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = services;

        await result.ExecuteAsync(context);

        context.Response.StatusCode.ShouldBe((int)expectedStatus);
        context.Response.ContentType.ShouldStartWith("text/html");
        context.Response.Body.Position = 0;
        var page = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        page.ShouldContain("BlokeBot");
        page.ShouldContain(expectedCopy);
        page.ShouldContain("No changes were made.");
        page.ShouldContain(expectedAction);
        page.ShouldContain("Close window");
    }

    private static HttpRequestMessage CallbackRequest(string path, string stateCookieName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"{stateCookieName}=state");
        return request;
    }

    private sealed class EndpointHost(
        WebApplication app,
        HttpClient client,
        SqliteBlokeBotDbFactory? dbFactory
    ) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<EndpointHost> StartAsync(
            bool configured,
            StubOAuthFlow? flow = null,
            bool isBotAdmin = true,
            AuthRole? selectedRole = null,
            string login = "admin",
            EndpointScenario endpointScenario = EndpointScenario.None
        )
        {
            var builder = WebApplication.CreateBuilder();
            var changes = new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>());
            RegisterUnselectedEndpointServices(builder.Services, changes);
            var configuredServices = await ConfigureEndpointScenarioAsync(
                builder.Services,
                changes,
                endpointScenario
            );
            builder.Services.AddSingleton(
                new TestAuthenticationSettings(
                    isBotAdmin,
                    selectedRole,
                    login,
                    configuredServices?.HostId ?? 1
                )
            );
            builder
                .Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    static _ => { }
                );
            builder.Services.AddAuthorization(options =>
                options.AddPolicy(
                    "BotAdmin",
                    policy =>
                        policy
                            .RequireAuthenticatedUser()
                            .AddRequirements(
                                new AuthSessionCapabilityRequirement(AuthSessionCapability.BotAdmin)
                            )
                )
            );
            builder.Services.AddSingleton<IAuthorizationHandler, AuthSessionCapabilityHandler>();
            builder.Services.AddSingleton<IOAuthFlow>(flow ?? new StubOAuthFlow(_authorizationUri));

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.UseAuthentication();
            app.UseAuthorization();
            if (configured)
            {
                app.MapBotOAuthEndpoints();
            }
            else
            {
                app.MapUnavailableBotOAuthEndpoint();
            }

            await app.StartAsync();
            var server = app.Services.GetRequiredService<IServer>();
            var address =
                server.Features.Get<IServerAddressesFeature>()?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException(
                    "The endpoint host did not publish an address."
                );
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(address),
            };
            return new EndpointHost(app, client, configuredServices?.DbFactory);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            if (dbFactory is not null)
            {
                await dbFactory.DisposeAsync();
            }
        }

        private static void RegisterUnselectedEndpointServices(
            IServiceCollection services,
            HostedChannelChangeNotifier changes
        )
        {
            services.AddSingleton(Uninitialized<HostBotAccountOAuthService>());
            services.AddSingleton(Uninitialized<HostBotAccountAuthorizationService>());
            services.AddSingleton(changes);
            services.AddSingleton(Uninitialized<ChannelBotOAuthService>());
            services.AddSingleton(Uninitialized<ChannelBotAuthorizationService>());
        }

        private static async Task<ConfiguredEndpointServices?> ConfigureEndpointScenarioAsync(
            IServiceCollection services,
            HostedChannelChangeNotifier changes,
            EndpointScenario scenario
        )
        {
            if (scenario is EndpointScenario.None)
            {
                return null;
            }

            var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
            var hostId = await SeedEndpointHostAsync(
                dbFactory,
                includeCustomBot: scenario is EndpointScenario.HostMissingPermission
            );
            switch (scenario)
            {
                case EndpointScenario.ChannelWrongAccount:
                    RegisterChannelServices(
                        services,
                        dbFactory,
                        changes,
                        "999",
                        "otherchannel",
                        ["channel:bot"]
                    );
                    break;
                case EndpointScenario.ChannelMissingPermission:
                    RegisterChannelServices(services, dbFactory, changes, "123", "streamer", []);
                    break;
                case EndpointScenario.HostMissingPermission:
                    RegisterHostServices(services, dbFactory, changes);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }

            return new(dbFactory, hostId);
        }

        private static void RegisterChannelServices(
            IServiceCollection services,
            SqliteBlokeBotDbFactory dbFactory,
            HostedChannelChangeNotifier changes,
            string grantUserId,
            string grantLogin,
            string[] grantedScopes
        )
        {
            var transport = new OAuthTransport(
                new EndpointOAuthHttpClientFactory(grantUserId, grantLogin, grantedScopes)
            );
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["TwitchBot:Identity:ClientId"] = "client",
                        ["TwitchBot:Identity:ClientSecret"] = "secret",
                        ["TwitchBot:ChannelAuthorization:Scopes:0"] = "channel:bot",
                    }
                )
                .Build();
            var oauth = new ChannelBotOAuthService(configuration, transport);
            services.AddSingleton(oauth);
            services.AddSingleton(new ChannelBotAuthorizationService(dbFactory, changes, oauth));
        }

        private static void RegisterHostServices(
            IServiceCollection services,
            SqliteBlokeBotDbFactory dbFactory,
            HostedChannelChangeNotifier changes
        )
        {
            var http = new EndpointOAuthHttpClientFactory("custom-id", "custombot", ["chat:read"]);
            var transport = new OAuthTransport(http);
            var helix = new HelixClient(http);
            var settings = BotSettings.FromOptions(
                new BotOptions
                {
                    Identity = new BotIdentityOptions
                    {
                        BotUsername = "mainbot",
                        ClientId = "client",
                        ClientSecret = "secret",
                        RedirectUri = "http://localhost/oauth/callback",
                        Scopes = ["chat:read", "chat:edit"],
                    },
                }
            );
            var oauth = new HostBotAccountOAuthService(settings, transport, helix);
            services.AddSingleton(oauth);
            services.AddSingleton(
                new HostBotAccountAuthorizationService(
                    dbFactory,
                    oauth,
                    transport,
                    helix,
                    new UnavailableTokenStatusSource(),
                    changes,
                    settings
                )
            );
        }

        private static async Task<int> SeedEndpointHostAsync(
            SqliteBlokeBotDbFactory dbFactory,
            bool includeCustomBot
        )
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var host = new BotHost
            {
                TwitchUserId = "123",
                Login = "streamer",
                DisplayName = "Streamer",
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
            if (includeCustomBot)
            {
                db.HostBotAccountSettings.Add(
                    new HostBotAccountSettings
                    {
                        HostId = host.Id,
                        OverrideEnabled = true,
                        UpdatedAtUtc = DateTime.UtcNow,
                    }
                );
                await db.SaveChangesAsync();
            }

            return host.Id;
        }

        private static TService Uninitialized<TService>()
            where TService : class
        {
            return (TService)RuntimeHelpers.GetUninitializedObject(typeof(TService));
        }

        private sealed record ConfiguredEndpointServices(
            SqliteBlokeBotDbFactory DbFactory,
            int HostId
        );
    }

    private sealed class StubOAuthFlow(Uri authorizationUri) : IOAuthFlow
    {
        public OAuthFlowCompletionOutcome CompletionOutcome { get; init; } =
            new OAuthFlowCompletionOutcome.Completed(
                new TokenSet("access", "refresh", DateTimeOffset.UtcNow.AddHours(1))
            );

        public Uri CreateAuthorizationUri()
        {
            return authorizationUri;
        }

        public Task<OAuthFlowCompletionOutcome> CompleteAuthorizationAsync(
            string code,
            string state,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(CompletionOutcome);
        }
    }

    private sealed class EndpointOAuthHttpClientFactory(
        string userId,
        string login,
        IReadOnlyList<string> scopes
    ) : IHttpClientFactory
    {
        private readonly Handler _handler = new(userId, login, scopes);

        public HttpClient CreateClient(string name)
        {
            return new(_handler, disposeHandler: false);
        }

        private sealed class Handler(string userId, string login, IReadOnlyList<string> scopes)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return Task.FromResult(
                    request.RequestUri?.AbsolutePath switch
                    {
                        "/oauth2/token" => JsonResponse(
                            """
                            {"access_token":"grant-token","refresh_token":"refresh","expires_in":3600}
                            """
                        ),
                        "/oauth2/validate" => JsonResponse(
                            JsonSerializer.Serialize(
                                new
                                {
                                    user_id = userId,
                                    login,
                                    scopes,
                                }
                            )
                        ),
                        "/helix/users" => JsonResponse(
                            JsonSerializer.Serialize(
                                new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            id = userId,
                                            login,
                                            display_name = login,
                                            profile_image_url = string.Empty,
                                        },
                                    },
                                }
                            )
                        ),
                        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                    }
                );
            }

            private static HttpResponseMessage JsonResponse(string json)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestAuthenticationSettings settings
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "BotAdminTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, $"{settings.Login}-id"),
                new(ClaimTypes.Name, settings.Login),
                new(AuthClaims.Login, settings.Login),
                new(AuthClaims.IsBotAdmin, settings.IsBotAdmin.ToString()),
            };
            if (settings.SelectedRole is { } role)
            {
                var channel = new BotHostChoice(
                    settings.SelectedHostId,
                    "streamer",
                    "Streamer",
                    role
                );
                claims.Add(new(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(channel)));
                claims.Add(new(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(channel)));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed record TestAuthenticationSettings(
        bool IsBotAdmin,
        AuthRole? SelectedRole,
        string Login,
        int SelectedHostId
    );

    private enum EndpointScenario
    {
        None,
        ChannelWrongAccount,
        ChannelMissingPermission,
        HostMissingPermission,
    }
}
