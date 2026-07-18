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
using BlokeBot.Core.Hosting;
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

public abstract class BotOAuthEndpointIntegrationTestBase
{
    private protected static readonly Uri AuthorizationUri = new(
        "https://id.twitch.tv/oauth2/authorize?state=test"
    );

    private protected static async Task AssertResultPageAsync(
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

    private protected static HttpRequestMessage CallbackRequest(string path, string stateCookieName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"{stateCookieName}=state");
        return request;
    }

    private protected sealed class EndpointHost(
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
            EndpointScenario endpointScenario = EndpointScenario.None,
            CallbackLogCapture? logs = null
        )
        {
            var builder = WebApplication.CreateBuilder();
            BlokeBotLogging.Configure(builder.Logging);
            if (logs is not null)
            {
                builder.Logging.ClearProviders();
                builder.Logging.AddProvider(logs);
            }
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
            builder.Services.AddSingleton<IOAuthFlow>(flow ?? new StubOAuthFlow(AuthorizationUri));

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
                case EndpointScenario.HostCustomBotDisabled:
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

    private protected sealed class StubOAuthFlow(Uri authorizationUri) : IOAuthFlow
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

    private protected sealed class EndpointOAuthHttpClientFactory(
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

    private protected sealed class TestAuthenticationHandler(
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

    private protected sealed record TestAuthenticationSettings(
        bool IsBotAdmin,
        AuthRole? SelectedRole,
        string Login,
        int SelectedHostId
    );

    private protected enum EndpointScenario
    {
        None,
        ChannelWrongAccount,
        ChannelMissingPermission,
        HostMissingPermission,
        HostCustomBotDisabled,
    }
}
