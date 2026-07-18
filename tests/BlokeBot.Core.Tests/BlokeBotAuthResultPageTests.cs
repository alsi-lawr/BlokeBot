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
    }

    [Test]
    public async Task Success_RenderingUsesOneStatusWithReturnAndCloseActions()
    {
        var (statusCode, page) = await RenderAsync(
            new BlokeBotAuthResult(
                BlokeBotAuthOutcome.Success,
                BlokeBotAuthStatus.Ok,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup,
                null
            )
        );

        statusCode.ShouldBe(StatusCodes.Status200OK);
        CountOccurrences(page, "role=\"status\"").ShouldBe(1);
        page.ShouldNotContain("role=\"alert\"");
        page.ShouldContain("href=\"/host\">Return to Channel setup</a>");
        page.ShouldContain("Close window</button>");
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

    private static int CountOccurrences(string value, string pattern)
    {
        return value.Split(pattern, StringSplitOptions.None).Length - 1;
    }
}
