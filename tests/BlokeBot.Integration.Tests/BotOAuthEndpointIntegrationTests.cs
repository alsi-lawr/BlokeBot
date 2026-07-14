using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BlokeBot.Auth.Sessions;
using BlokeBot.BotRuntime;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Twitch.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Integration.Tests;

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
    public async Task UnavailableBotOAuth_AuthenticatedBotAdminStarting_ReturnsSetupBadRequest()
    {
        await using var host = await EndpointHost.StartAsync(configured: false);

        using var response = await host.Client.GetAsync("/oauth/start");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<string>()).ShouldBe(
            "The bot account is not set up yet."
        );
        response.Headers.Location.ShouldBeNull();
    }

    private sealed class EndpointHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<EndpointHost> StartAsync(bool configured)
        {
            var builder = WebApplication.CreateBuilder();
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
            builder.Services.AddSingleton<IOAuthFlow>(new StubOAuthFlow(_authorizationUri));
            RegisterUnselectedEndpointServices(builder.Services);

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
            return new EndpointHost(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }

        private static void RegisterUnselectedEndpointServices(IServiceCollection services)
        {
            services.AddSingleton<HostBotAccountOAuthService>(static _ => null!);
            services.AddSingleton<HostBotAccountAuthorizationService>(static _ => null!);
            services.AddSingleton<HostedChannelChangeNotifier>(static _ => null!);
            services.AddSingleton<ChannelBotOAuthService>(static _ => null!);
            services.AddSingleton<ChannelBotAuthorizationService>(static _ => null!);
        }
    }

    private sealed class StubOAuthFlow(Uri authorizationUri) : IOAuthFlow
    {
        public Uri CreateAuthorizationUri()
        {
            return authorizationUri;
        }

        public Task<TokenSet> CompleteAuthorizationAsync(
            string code,
            string state,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new TokenSet("access", "refresh", DateTimeOffset.UtcNow.AddHours(1))
            );
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "BotAdminTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "admin-id"),
                    new Claim(ClaimTypes.Name, "admin"),
                    new Claim(AuthClaims.Login, "admin"),
                    new Claim(AuthClaims.IsBotAdmin, "true"),
                ],
                Scheme.Name
            );
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
