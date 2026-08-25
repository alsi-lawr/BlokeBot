using AngleSharp.Dom;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPassportUiTests
{
    [Test]
    public async Task SignedInDirectRoute_WhenDisabled_ShowsRecoveryWithoutRetainedData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(
            database,
            HostFeatureFlags.None,
            ViewerPassportVisibility.Public,
            "RETAINED-PROFILE-LINE"
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<IHostStreamLivenessProvider>(new OfflineStreams());
        _ = context.Services.AddSingleton<ViewerPassportService>();

        var cut = context.Render<ViewerPassportsPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("/host#chat-tools");
            cut.Markup.ShouldNotContain("RETAINED-PROFILE-LINE");
        });
    }

    [Test]
    public async Task PublicRoute_PrivatePassport_ExposesNoProfileFields()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedAsync(
            database,
            HostFeatureFlags.ViewerPassports,
            ViewerPassportVisibility.Private,
            "PRIVATE-PROFILE-LINE"
        );
        using var context = new BunitContext();
        _ = context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        _ = context.Services.AddSingleton<IHostStreamLivenessProvider>(new OfflineStreams());
        _ = context.Services.AddSingleton<ViewerPassportService>();
        _ = context.Services.AddSingleton(new BlokeBotPageContextAccessor());
        _ = context.AddAuthorization().SetNotAuthorized();

        var cut = context.Render<PublicViewerPassportPage>(parameters =>
            parameters.Add(page => page.Channel, "streamer").Add(page => page.Viewer, "streamer")
        );

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("This viewer passport is not available");
            cut.Markup.ShouldNotContain("PRIVATE-PROFILE-LINE");
        });
    }

    [Test]
    public async Task PublicRoute_ProfileLine_IsRenderedAsText()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedAsync(
            database,
            HostFeatureFlags.ViewerPassports,
            ViewerPassportVisibility.Public,
            "<script>alert('unsafe')</script>"
        );
        using var context = new BunitContext();
        _ = context.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        _ = context.Services.AddSingleton<IHostStreamLivenessProvider>(new OfflineStreams());
        _ = context.Services.AddSingleton<ViewerPassportService>();
        _ = context.Services.AddSingleton(new BlokeBotPageContextAccessor());
        _ = context.AddAuthorization().SetNotAuthorized();

        var cut = context.Render<PublicViewerPassportPage>(parameters =>
            parameters.Add(page => page.Channel, "streamer").Add(page => page.Viewer, "streamer")
        );

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("script").ShouldBeEmpty();
            cut.Markup.ShouldContain("&lt;script&gt;alert('unsafe')&lt;/script&gt;");
        });
    }

    [Test]
    public async Task ChoosingVisibility_MovesTheSelectedStateAndPersistsTheChosenValue()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(
            database,
            HostFeatureFlags.ViewerPassports,
            ViewerPassportVisibility.ChannelMembers,
            "PROFILE-LINE"
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(new PointBalanceService(database));
        _ = context.Services.AddSingleton<IHostStreamLivenessProvider>(new OfflineStreams());
        _ = context.Services.AddSingleton<ViewerPassportService>();

        var cut = context.Render<ViewerPassportsPage>();

        cut.WaitForAssertion(() =>
            Choice(cut, ViewerPassportVisibility.ChannelMembers)
                .ClassList.ShouldContain("passport-visibility-option--selected")
        );
        Radio(cut, ViewerPassportVisibility.Private).Change(true);

        cut.WaitForAssertion(() =>
            Choice(cut, ViewerPassportVisibility.Private)
                .ClassList.ShouldContain("passport-visibility-option--selected")
        );
        cut.FindAll(".passport-visibility-option--selected").Count.ShouldBe(1);
        var save = cut.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save passport");
        save.Click();

        cut.WaitForAssertion(() => _ = cut.Find("[role='status']"));
        await using var db = await database.CreateDbContextAsync();
        var saved = await db.ViewerPassports.SingleAsync();
        saved.Visibility.ShouldBe(ViewerPassportVisibility.Private);
        saved.ProfileLine.ShouldBe("PROFILE-LINE");
    }

    private static IElement Choice(
        IRenderedComponent<ViewerPassportsPage> cut,
        ViewerPassportVisibility visibility
    ) => Radio(cut, visibility).Closest(".passport-visibility-option")!;

    private static IElement Radio(
        IRenderedComponent<ViewerPassportsPage> cut,
        ViewerPassportVisibility visibility
    ) => cut.Find($"input[name='passport-visibility'][value='{visibility}']");

    private sealed class OfflineStreams : IHostStreamLivenessProvider
    {
        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
            IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(
                    Result<HostStreamLivenessOutcome, Never>.Success(
                        new HostStreamLivenessOutcome.Offline()
                    )
                )
            );
    }

    private static async Task<int> SeedAsync(
        SqliteBlokeBotDbFactory database,
        HostFeatureFlags features,
        ViewerPassportVisibility visibility,
        string profileLine
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = features,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var passport = new ViewerPassport
        {
            HostId = host.Id,
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            ProfileLine = profileLine,
            Visibility = visibility,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _ = db.ViewerPassports.Add(passport);
        _ = await db.SaveChangesAsync();
        _ = db.ViewerPassportLogins.Add(
            new()
            {
                HostId = host.Id,
                PassportId = passport.Id,
                Login = passport.Login,
                FirstSeenAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
