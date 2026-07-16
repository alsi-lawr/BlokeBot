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

public sealed class HostBotOAuthEndpointTests : BotOAuthEndpointIntegrationTestBase
{
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
}
