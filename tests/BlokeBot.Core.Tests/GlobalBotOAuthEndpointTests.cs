using System.Net;
using Shouldly;

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
    }

    [Test]
    public async Task CancelledGlobalOAuth_AccessDenied_RedactsProviderMessage()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/callback?error=access_denied");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldNotContain("access_denied");
    }

    [Test]
    public async Task GlobalOAuth_UnexpectedProviderError_ReturnsTemporaryFailureWithSupportReference()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/callback?error=provider-secret");

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldNotContain("provider-secret");
    }

    [Test]
    public async Task GlobalOAuth_Completed_ReturnsBotAccountSpecificSuccess()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/callback?code=code&state=state");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task ConfiguredBotOAuth_AuthenticatedNonAdminStarting_ReturnsAdministratorGuidance()
    {
        await using var host = await EndpointHost.StartAsync(configured: true, isBotAdmin: false);

        using var response = await host.Client.GetAsync("/oauth/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
