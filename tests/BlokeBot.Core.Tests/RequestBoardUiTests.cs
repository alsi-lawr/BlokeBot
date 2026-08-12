using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

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
        var accepted = (
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
                accepted.Id,
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
        _ = await service.ModerateAsync(
            hostId,
            new ModerateRequestCommand(
                accepted.Id,
                RequestSubmissionStatus.Accepted,
                "Ready for voting.",
                "PRIVATE-MODERATOR-NOTE",
                "PRIVATE-REJECTION-REASON",
                -10,
                "Clips",
                ["review"]
            ),
            CancellationToken.None
        );
        var queued = (
            await service.SubmitAsync(
                hostId,
                "clips",
                new SubmitRequestCommand(
                    Guid.NewGuid(),
                    "another_viewer",
                    "Higher priority queued request",
                    "Clips",
                    ["review"],
                    new Dictionary<string, string>
                    {
                        ["clip"] = "https://clips.twitch.tv/AnotherGoodClip",
                    }
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
                queued.Id,
                RequestSubmissionStatus.Approved,
                "",
                "",
                "",
                100,
                "Clips",
                ["review"]
            ),
            CancellationToken.None
        );
        _ = await service.ModerateAsync(
            hostId,
            new ModerateRequestCommand(
                queued.Id,
                RequestSubmissionStatus.Queued,
                "",
                "",
                "",
                100,
                "Clips",
                ["review"]
            ),
            CancellationToken.None
        );
        using var context = new BunitContext();
        _ = context.Services.AddSingleton(service);

        var page = context.Render<PublicRequestBoardPage>(parameters =>
            parameters
                .Add(component => component.Channel, "streamer")
                .Add(component => component.BoardSlug, "clips")
        );

        page.WaitForAssertion(() => page.Find("h1").TextContent.ShouldBe("Clip reviews"));
        page.Markup.ShouldContain("Up to 5 votes per viewer");
        page.Markup.ShouldNotContain("PRIVATE-MODERATOR-NOTE");
        page.Markup.ShouldNotContain("PRIVATE-REJECTION-REASON");
        page.Markup.ShouldNotContain("<script>alert");
        var link = page.Find("a[href='https://clips.twitch.tv/GoodClip']");
        link.GetAttribute("rel").ShouldBe("noopener noreferrer");
        page.FindAll("article.card h3")
            .Select(heading => heading.TextContent)
            .ShouldBe(["<script>alert('title')</script>", "Higher priority queued request"]);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
