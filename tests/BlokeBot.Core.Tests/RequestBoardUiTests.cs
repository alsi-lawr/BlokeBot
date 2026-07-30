using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class RequestBoardUiTests
{
    [Test]
    public async Task PublicBoard_RendersVisibleRulesEscapedLinksAndNoPrivateFields()
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
                "clips",
                "Clip reviews",
                "Share a clip.",
                true,
                "0",
                RequestBoardRefundPolicy.Never,
                3,
                0,
                5,
                true,
                [
                    new RequestBoardFieldCommand(
                        "clip",
                        "Clip",
                        RequestBoardFieldKind.TwitchClip,
                        true,
                        2048
                    ),
                ]
            ),
            CancellationToken.None
        );
        var submission = (
            await service.SubmitAsync(
                hostId,
                "clips",
                new SubmitRequestCommand(
                    Guid.NewGuid(),
                    "viewer",
                    "<script>alert('title')</script>",
                    "Clips",
                    ["review"],
                    new Dictionary<string, string> { ["clip"] = "https://clips.twitch.tv/GoodClip" }
                ),
                CancellationToken.None
            )
        ).Match(
            value => value.Value,
            rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );
        _ = await service.ModerateAsync(
            hostId,
            new ModerateRequestCommand(
                submission.Id,
                RequestSubmissionStatus.Approved,
                "Ready for voting.",
                "PRIVATE-MODERATOR-NOTE",
                "PRIVATE-REJECTION-REASON",
                0,
                "Clips",
                ["review"]
            ),
            CancellationToken.None
        );
        using var context = new BunitContext();
        context.Services.AddSingleton(service);

        var page = context.Render<PublicRequestBoardPage>(parameters =>
            parameters
                .Add(component => component.Channel, "streamer")
                .Add(component => component.BoardSlug, "clips")
        );

        page.WaitForAssertion(() => page.Find("h1").TextContent.ShouldBe("Clip reviews"));
        page.Markup.ShouldContain("Up to 5 votes per viewer");
        page.Markup.ShouldContain("Higher priority first");
        page.Markup.ShouldNotContain("PRIVATE-MODERATOR-NOTE");
        page.Markup.ShouldNotContain("PRIVATE-REJECTION-REASON");
        page.Markup.ShouldNotContain("<script>alert");
        var link = page.Find("a[href='https://clips.twitch.tv/GoodClip']");
        link.GetAttribute("rel").ShouldBe("noopener noreferrer");
        page.FindAll("article.card").ShouldHaveSingleItem();
    }

    [Test]
    public void ModeratorAndPublicRoutes_DeclareCorrectAuthorizationAudience()
    {
        var moderator = typeof(RequestBoardsPage)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ShouldHaveSingleItem();
        var publicRoute = typeof(PublicRequestBoardPage)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
            .ShouldHaveSingleItem();

        moderator.Policy.ShouldBe("HostSelected");
        publicRoute.ShouldNotBeNull();
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
