using System.Net;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostBotOAuthEndpointTests : BotOAuthEndpointIntegrationTestBase
{
    [Test]
    public async Task HostBotOAuth_AdminManagingChannel_CanStartCustomBotFlow()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Admin,
            login: "administrator",
            endpointScenario: EndpointScenario.HostMissingPermission
        );

        using var startResponse = await host.Client.GetAsync("/oauth/host-bot/start");

        startResponse.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        _ = startResponse.Headers.Location.ShouldNotBeNull();
        startResponse.Headers.Location.Host.ShouldBe("id.twitch.tv");
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

        var state = host.IssueHostBotState();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/oauth/callback?code=code&state={state}"
        );
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BroadcasterOAuth_SelectionChangedBeforeCallback_DoesNotPersistAuthorization()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.BroadcasterAuthorization,
            selectedHostId: 2
        );
        var state = host.IssueBroadcasterState(1);

        using var response = await host.Client.GetAsync($"/oauth/callback?code=code&state={state}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using var db = await host.DbFactory!.CreateDbContextAsync();
        (await db.HostBroadcasterAuthorizations.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task BroadcasterOAuth_InvalidState_RetriesOnlyBroadcasterAuthorization()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.BroadcasterAuthorization
        );

        using var response = await host.Client.GetAsync(
            "/oauth/callback?code=code&state=broadcaster.invalid"
        );

        await AssertBroadcasterRetryAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BroadcasterOAuth_Denied_RetriesOnlyBroadcasterAuthorization()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.BroadcasterAuthorization
        );
        var state = host.IssueBroadcasterState(1);

        using var response = await host.Client.GetAsync(
            $"/oauth/callback?error=access_denied&state={state}"
        );

        await AssertBroadcasterRetryAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BroadcasterOAuth_MissingCode_RetriesOnlyBroadcasterAuthorization()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.BroadcasterAuthorization
        );
        var state = host.IssueBroadcasterState(1);

        using var response = await host.Client.GetAsync($"/oauth/callback?state={state}");

        await AssertBroadcasterRetryAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BroadcasterOAuth_WrongAccountOrScope_RetriesOnlyBroadcasterAuthorization()
    {
        await AssertBroadcasterCompletionRetryAsync(EndpointScenario.BroadcasterWrongAccount);
        await AssertBroadcasterCompletionRetryAsync(EndpointScenario.BroadcasterMissingPermission);
    }

    [Test]
    public async Task BroadcasterOAuth_ProviderFailures_RetryOnlyBroadcasterAuthorization()
    {
        await AssertBroadcasterCompletionRetryAsync(
            EndpointScenario.BroadcasterProviderNotValidated
        );
        await AssertBroadcasterCompletionRetryAsync(
            EndpointScenario.BroadcasterTransportFailure,
            HttpStatusCode.BadGateway
        );
    }

    [Test]
    public async Task BroadcasterOAuth_CompleteOwnerGrant_PersistsProtectedAuthorization()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.BroadcasterAuthorization
        );
        var state = host.IssueBroadcasterState(1);

        using var response = await host.Client.GetAsync($"/oauth/callback?code=code&state={state}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldNotContain(">Try again</a>");
        await using var db = await host.DbFactory!.CreateDbContextAsync();
        var authorization = await db.HostBroadcasterAuthorizations.SingleAsync();
        authorization.TwitchUserId.ShouldBe("123");
        _ = authorization.ProtectedTokenPayload.ShouldNotBeNull();
        _ = authorization.AuthorizedScopes.ShouldNotBeNull();
        HostBroadcasterAuthorizationService.MilestoneScopes.ShouldAllBe(scope =>
            authorization.AuthorizedScopes.Contains(scope, StringComparison.Ordinal)
        );
    }

    [Test]
    public async Task HostBotOAuth_CustomBotDisabledStarting_ReturnsEnableCustomBotGuidance()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.HostCustomBotDisabled
        );

        using var response = await host.Client.GetAsync("/oauth/host-bot/start");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task AssertBroadcasterCompletionRetryAsync(
        EndpointScenario scenario,
        HttpStatusCode expectedStatus = HttpStatusCode.BadRequest
    )
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: scenario
        );
        var state = host.IssueBroadcasterState(1);

        using var response = await host.Client.GetAsync($"/oauth/callback?code=code&state={state}");

        await AssertBroadcasterRetryAsync(response, expectedStatus);
        await using var db = await host.DbFactory!.CreateDbContextAsync();
        (await db.HostBroadcasterAuthorizations.CountAsync()).ShouldBe(0);
    }

    private static async Task AssertBroadcasterRetryAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus
    )
    {
        response.StatusCode.ShouldBe(expectedStatus);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("href=\"/oauth/broadcaster/start\">Try again</a>");
        page.ShouldNotContain("href=\"/oauth/host-bot/start\"");
        page.ShouldNotContain("href=\"/oauth/channel-bot/start\"");
        page.ShouldNotContain("href=\"/oauth/start\"");
    }
}
