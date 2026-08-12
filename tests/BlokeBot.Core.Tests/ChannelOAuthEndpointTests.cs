using System.Net;
using BlokeBot.Core.Auth.Sessions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ChannelOAuthEndpointTests : BotOAuthEndpointIntegrationTestBase
{
    [Test]
    public async Task ChannelOAuth_NoSelectedChannel_ReturnsExactChannelGuidance()
    {
        await using var host = await EndpointHost.StartAsync(configured: true);

        using var response = await host.Client.GetAsync("/oauth/channel-bot/start");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
        page.ShouldNotContain("provider-secret");
    }
}
