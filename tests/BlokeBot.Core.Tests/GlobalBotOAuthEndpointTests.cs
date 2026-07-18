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

public sealed class GlobalBotOAuthEndpointTests : BotOAuthEndpointIntegrationTestBase
{
    [Test]
    public async Task ConfiguredBotOAuth_AuthenticatedBotAdminStarting_RedirectsToAuthorization()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldBe(AuthorizationUri);
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
        var flow = new StubOAuthFlow(AuthorizationUri)
        {
            CompletionOutcome = new OAuthFlowCompletionOutcome.InvalidState(),
        };
        await using var host = await EndpointHost.StartAsync(configured: true, flow);

        using var response = await host.Client.GetAsync("/oauth/callback?code=code&state=replayed");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Connection link expired");
        page.ShouldContain("This Twitch connection link is no longer valid.");
        page.ShouldContain("No changes were made.");
        page.ShouldContain("Try again");
        page.ShouldContain("Return to Admin");
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
        page.ShouldContain("Twitch did not finish this connection.");
        page.ShouldContain("Return to Admin");
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
        page.ShouldContain("BlokeBot could not finish this connection right now.");
        page.ShouldContain("Support reference:");
        page.ShouldContain("Get help");
        page.ShouldContain("Return to Admin");
        page.ShouldContain("role=\"alert\"");
        page.ShouldContain("href=\"/oauth/start\">Try again</a>");
        page.ShouldContain("type=\"button\" onclick=\"window.close()\">Close window</button>");
        page.ShouldNotContain("provider-secret");
    }

    [Test]
    public async Task GlobalOAuth_Completed_ReturnsBotAccountSpecificSuccess()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/callback?code=code&state=state");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Bot account connected");
        page.ShouldContain("The bot account connection was saved.");
        page.ShouldContain("Return to Admin");
    }

    [Test]
    public async Task ConfiguredBotOAuth_AuthenticatedNonAdminStarting_ReturnsAdministratorGuidance()
    {
        await using var host = await EndpointHost.StartAsync(configured: true, isBotAdmin: false);

        using var response = await host.Client.GetAsync("/oauth/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Access required");
        page.ShouldContain("Return to Admin");
    }
}
