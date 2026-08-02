using System.Text;
using BlokeBot.Core.Auth;
using BlokeBot.Core.Auth.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class BlokeBotAuthResultPageTests
{
    [Test]
    public async Task ProviderFailure_RenderingUsesOneAlertWithResponsiveThemeAndActions()
    {
        var (statusCode, page) = await RenderAsync(
            new BlokeBotAuthResult(
                BlokeBotAuthOutcome.ProviderUnavailable,
                BlokeBotAuthStatus.BadGateway,
                BlokeBotAuthRetryAction.ChannelBot,
                BlokeBotAuthReturnAction.ChannelSetup,
                "ref-42"
            )
        );

        statusCode.ShouldBe(StatusCodes.Status502BadGateway);
        CountOccurrences(page, "role=\"alert\"").ShouldBe(1);
        page.ShouldNotContain("role=\"status\"");
        page.ShouldContain("const storageKey = \"blokebot.theme\";");
        page.ShouldContain("html[data-theme=\"dark\"]");
        page.ShouldContain("@media (min-width: 30rem)");
        page.ShouldContain("href=\"/oauth/channel-bot/start\">Try again</a>");
        page.ShouldContain("href=\"/host\">Return to Channel setup</a>");
        page.ShouldContain("type=\"button\" onclick=\"window.close()\">Close window</button>");
        page.ShouldContain("Support reference: <code>ref-42</code>");

        var (_, expiredPage) = await RenderAsync(
            new BlokeBotAuthResult(
                BlokeBotAuthOutcome.InvalidOrExpired,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.SignIn,
                BlokeBotAuthReturnAction.SignIn,
                null
            )
        );
        expiredPage.ShouldContain("Connection link expired");
        page.ShouldNotContain("Connection link expired");
    }

    [Test]
    public async Task BroadcasterRetry_RenderingTargetsOnlyBroadcasterAuthorization()
    {
        var (_, page) = await RenderAsync(
            new BlokeBotAuthResult(
                BlokeBotAuthOutcome.PermissionOrAccount,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.Broadcaster,
                BlokeBotAuthReturnAction.ChannelSetup,
                null
            )
        );

        page.ShouldContain("href=\"/oauth/broadcaster/start\">Try again</a>");
        page.ShouldNotContain("href=\"/oauth/host-bot/start\"");
        page.ShouldNotContain("href=\"/oauth/channel-bot/start\"");
        page.ShouldNotContain("href=\"/oauth/start\"");
    }

    [Test]
    public async Task Success_RenderingUsesOneStatusWithReturnAndCloseActions()
    {
        var (statusCode, botAccountPage) = await RenderAsync(
            new BlokeBotAuthResult(
                BlokeBotAuthOutcome.Success,
                BlokeBotAuthStatus.Ok,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup,
                null,
                new BlokeBotAuthContext.Success(BlokeBotAuthSuccessKind.BotAccount)
            )
        );

        statusCode.ShouldBe(StatusCodes.Status200OK);
        CountOccurrences(botAccountPage, "role=\"status\"").ShouldBe(1);
        botAccountPage.ShouldNotContain("role=\"alert\"");
        botAccountPage.ShouldContain("Bot account connected");
        botAccountPage.ShouldContain("The bot account connection was saved.");
        botAccountPage.ShouldContain("href=\"/host\">Return to Channel setup</a>");
        botAccountPage.ShouldContain("Close window</button>");

        var (_, channelPage) = await RenderAsync(
            new BlokeBotAuthResult(
                BlokeBotAuthOutcome.Success,
                BlokeBotAuthStatus.Ok,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup,
                null,
                new BlokeBotAuthContext.Success(BlokeBotAuthSuccessKind.ChannelConnection)
            )
        );
        channelPage.ShouldContain("Twitch access for this channel was saved.");
    }

    [Test]
    public async Task ExceptionBackedProviderFailure_RedactsTheExceptionMessageAndLogsSafeFields()
    {
        const string Sentinel = "exception-sentinel-secret";
        var context = new DefaultHttpContext { TraceIdentifier = "support-ref" };
        var logger = new RecordingLogger<WebAuthEndpointLog>();
        var error = WebAuthenticationError.InvalidProviderPayload.From(
            new InvalidOperationException(Sentinel)
        );

        var result = AuthEndpoints.MapAuthenticationError(error, context, logger);
        var (statusCode, page) = await RenderAsync(result);

        statusCode.ShouldBe(StatusCodes.Status502BadGateway);
        page.ShouldNotContain(Sentinel);
        page.ShouldContain("Support reference: <code>support-ref</code>");
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Properties["Classification"].ShouldBe("InvalidProviderPayload");
        entry.Properties["FailureType"].ShouldBe("InvalidOperationException");
        entry.Message.ShouldNotContain(Sentinel);
    }

    [Test]
    public async Task ContextualFailures_RenderSpecificGuidanceWithAnHtmlEncodedChannelLogin()
    {
        var (_, noChannelPage) = await RenderAsync(
            new BlokeBotAuthResult(
                BlokeBotAuthOutcome.NoChannelSelected,
                BlokeBotAuthStatus.Forbidden,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup,
                null
            )
        );
        var (_, disabledPage) = await RenderAsync(
            new BlokeBotAuthResult(
                BlokeBotAuthOutcome.CustomBotDisabled,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup,
                null
            )
        );
        var (_, wrongAccountPage) = await RenderAsync(
            new BlokeBotAuthResult(
                BlokeBotAuthOutcome.WrongAccount,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.ChannelBot,
                BlokeBotAuthReturnAction.ChannelSetup,
                null,
                new BlokeBotAuthContext.RequiredChannel("<streamer>&")
            )
        );

        noChannelPage.ShouldContain("Choose a channel to continue");
        noChannelPage.ShouldContain("Open Channel setup");
        disabledPage.ShouldContain("Turn on the custom bot first");
        disabledPage.ShouldContain("Enable the custom bot in Channel setup");
        wrongAccountPage.ShouldContain("@&lt;streamer&gt;&amp; is the Twitch account needed");
        wrongAccountPage.ShouldNotContain("@<streamer>& is the Twitch account needed");
    }

    private static async Task<(int StatusCode, string Page)> RenderAsync(IResult result)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() },
        };

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var page = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (context.Response.StatusCode, page);
    }

    private static int CountOccurrences(string value, string pattern) =>
        value.Split(pattern, StringSplitOptions.None).Length - 1;
}
