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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

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
        startResponse.Headers.Location.ShouldNotBeNull();
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
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Twitch access needed");
        page.ShouldContain("approve every requested permission");
        page.ShouldContain("Return to Channel setup");
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
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Turn on the custom bot first");
        page.ShouldContain("Enable the custom bot in Channel setup");
        page.ShouldContain("Return to Channel setup");
    }
}
