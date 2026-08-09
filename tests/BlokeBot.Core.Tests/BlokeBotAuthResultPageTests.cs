using System.Text;
using BlokeBot.Core.Auth.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BlokeBotAuthResultPageTests
{
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
}
