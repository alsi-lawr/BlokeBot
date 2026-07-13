using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.History;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Toasts;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.UI.Tests;

public sealed class GuessingDashboardRejectionTests
{
    [Test]
    public async Task PublicChatRejected_StartingRound_ShowsWarningWithoutDeliverySuccess()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedGuessingAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var chat = new RejectingChatSender();
        context.Services.AddSingleton<IPublicChatMessageSender>(chat);
        context.Services.AddSingleton<GuessingDashboardService>();
        context.Services.AddSingleton<GuessingHistoryService>();
        context.Services.AddSingleton<GuessingChangeNotifier>();
        context.Services.AddSingleton<PointBalanceService>();
        context.Services.AddSingleton<PointsChangeNotifier>();
        context.Services.AddSingleton<GuessingRoundService>();
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<GuessingDashboard>();

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Start round").Click();

        var sent = chat.Messages.ShouldHaveSingleItem();
        sent.Channel.ShouldBe("streamer");
        sent.Message.ShouldContain("Private round guessing is open.");
        chat.Deadlines.ShouldHaveSingleItem()
            .ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
        var warning = toasts.Current.ShouldHaveSingleItem();
        warning.Kind.ShouldBe(ToastKind.Warning);
        warning.Message.ShouldBe("The action completed, but its chat message could not be queued.");
        warning.Message.ShouldNotContain(sent.Message);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.Rounds.SingleAsync()).Status.ShouldBe(GuessRoundStatus.Open);
    }

    private static async Task<int> SeedGuessingAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc),
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        db.Profiles.Add(
            new GuessRoundProfile
            {
                HostId = host.Id,
                Name = "Private round",
                Slug = "private-round",
                IsDefault = true,
                ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
            }
        );
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class RejectingChatSender : IPublicChatMessageSender
    {
        internal List<SentMessage> Messages { get; } = [];

        internal List<PublicChatDeliveryDeadline> Deadlines { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(new SentMessage(channel, message));
            Deadlines.Add(deadline);
            return ValueTask.FromResult<PublicChatSendOutcome>(
                new PublicChatSendOutcome.Rejected()
            );
        }
    }

    private sealed record SentMessage(string Channel, string Message);
}
