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

public sealed class ChannelOAuthEndpointTests : BotOAuthEndpointIntegrationTestBase
{
    [Test]
    public async Task ChannelOAuth_NoSelectedChannel_ReturnsExactChannelGuidance()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/channel-bot/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("Choose a channel to continue");
        page.ShouldContain("Open Channel setup, choose your channel");
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
        page.ShouldContain("You do not have access to complete this Twitch connection.");
    }

    [Test]
    public async Task ChannelOAuth_AdminManagingChannelStarting_RemainsOwnerOnly()
    {
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Admin,
            login: "administrator"
        );

        using var response = await host.Client.GetAsync("/oauth/channel-bot/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var page = await response.Content.ReadAsStringAsync();
        page.ShouldContain("You do not have access to complete this Twitch connection.");
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
        page.ShouldContain("Use the channel account");
        page.ShouldContain("@streamer is the Twitch account needed for this channel.");
        page.ShouldContain("Reconnect using that channel account.");
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
        page.ShouldContain("Twitch access needed");
        page.ShouldContain("approve every requested permission");
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
}
