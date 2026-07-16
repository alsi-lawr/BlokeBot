using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using BlokeBot.Auth.Sessions;
using BlokeBot.BotRuntime;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Hosts;
using BlokeBot.Twitch.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
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
        page.ShouldContain("No changes were made.");
        page.ShouldContain("Try again");
        page.ShouldContain("Return to Channel setup");
        page.ShouldContain("Close window");
    }

    [Test]
    public async Task CancelledGlobalOAuth_AccessDenied_RedactsProviderMessage()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/callback?error=access_denied");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Connection cancelled");
        page.ShouldNotContain("access_denied");
    }

    [Test]
    public async Task GlobalOAuth_UnexpectedProviderError_ReturnsTemporaryFailureWithSupportReference()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/callback?error=provider-secret");

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Twitch is temporarily unavailable");
        page.ShouldContain("Support reference:");
        page.ShouldContain("Get help");
        page.ShouldNotContain("provider-secret");
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

    private sealed class EndpointHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<EndpointHost> StartAsync(
            bool configured,
            StubOAuthFlow? flow = null,
            bool isBotAdmin = true,
            AuthRole? selectedRole = null,
            string login = "admin"
        )
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(
                new TestAuthenticationSettings(isBotAdmin, selectedRole, login)
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
            services.AddSingleton(Uninitialized<HostBotAccountOAuthService>());
            services.AddSingleton(Uninitialized<HostBotAccountAuthorizationService>());
            services.AddSingleton(Uninitialized<HostedChannelChangeNotifier>());
            services.AddSingleton(Uninitialized<ChannelBotOAuthService>());
            services.AddSingleton(Uninitialized<ChannelBotAuthorizationService>());
        }

        private static TService Uninitialized<TService>()
            where TService : class
        {
            return (TService)RuntimeHelpers.GetUninitializedObject(typeof(TService));
        }
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
                var channel = new BotHostChoice(1, "streamer", "Streamer", role);
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
        string Login
    );
}
