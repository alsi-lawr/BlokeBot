using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class RequestBoardUiTests
{
    [Test]
    public async Task PublicBoard_UsesVerifiedSessionForWritesAndExactSelfWithdrawalAfterRename()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var service = new RequestBoardService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = await service.ConfigureAsync(
            hostId,
            new ConfigureRequestBoardCommand(
                "games",
                "Games",
                "",
                true,
                "0",
                RequestBoardRefundPolicy.Never,
                3,
                0,
                3,
                true,
                [
                    new RequestBoardFieldCommand(
                        "details",
                        "Details",
                        RequestBoardFieldKind.Text,
                        false,
                        100
                    ),
                ]
            ),
            CancellationToken.None
        );
        using (var original = AuthorizedContext(service, "verified-session-id", "original"))
        {
            var page = RenderBoard(original);
            _ = page.WaitForElement("#request-board-title");
            await page.Find("#request-board-title").InputAsync(new() { Value = "Web request" });
            await page.Find("button.btn-primary").ClickAsync(new());
            page.WaitForAssertion(() =>
                page.Find("article h3").TextContent.ShouldBe("Web request")
            );
        }
        await using (var verify = await database.CreateDbContextAsync())
        {
            var submission = await verify.RequestSubmissions.SingleAsync();
            submission.SubmitterTwitchUserId.ShouldBe("verified-session-id");
            submission.SubmitterLogin.ShouldBe("original");
        }
        using (var reclaimed = AuthorizedContext(service, "different-id", "original"))
        {
            var page = RenderBoard(reclaimed);
            _ = page.WaitForElement("article h3");
            page.FindAll("button").Any(button => button.TextContent == "Withdraw").ShouldBeFalse();
        }
        using (var missingId = AuthorizedContext(service, "", "original"))
        {
            var page = RenderBoard(missingId);
            _ = page.WaitForElement("article h3");
            page.FindAll("button")
                .Any(button => button.TextContent is "Withdraw" or "Submit request" or "Vote")
                .ShouldBeFalse();
        }
        using (var renamed = AuthorizedContext(service, "verified-session-id", "renamed"))
        {
            var page = RenderBoard(renamed);
            page.WaitForAssertion(() =>
                page.FindAll("button")
                    .Any(button => button.TextContent == "Withdraw")
                    .ShouldBeTrue()
            );
            await page.FindAll("button")
                .Single(button => button.TextContent == "Withdraw")
                .ClickAsync(new());
            page.WaitForAssertion(() =>
                page.FindAll("button")
                    .Any(button => button.TextContent == "Withdraw")
                    .ShouldBeFalse()
            );
            page.Markup.ShouldNotContain("verified-session-id");
        }
        await using var final = await database.CreateDbContextAsync();
        (await final.RequestSubmissions.SingleAsync()).Status.ShouldBe(
            RequestSubmissionStatus.Withdrawn
        );
    }

    private static BunitContext AuthorizedContext(
        RequestBoardService service,
        string userId,
        string login
    )
    {
        var context = new BunitContext();
        _ = context.Services.AddSingleton(service);
        var authorization = context.AddAuthorization();
        _ = authorization.SetAuthorized(login);
        _ = authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(AuthClaims.Login, login)
        );
        return context;
    }

    private static IRenderedComponent<PublicRequestBoardPage> RenderBoard(BunitContext context) =>
        context.Render<PublicRequestBoardPage>(parameters =>
            parameters
                .Add(component => component.Channel, "streamer")
                .Add(component => component.BoardSlug, "games")
        );
}
