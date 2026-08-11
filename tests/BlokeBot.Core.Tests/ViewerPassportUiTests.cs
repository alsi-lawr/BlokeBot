using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPassports;
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
        _ = context.Services.AddSingleton<ViewerPassportService>();

        var cut = context.Render<ViewerPassportsPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Viewer passports is off for this channel");
            cut.Markup.ShouldContain("/host#chat-tools");
            cut.Markup.ShouldContain("retained");
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
        _ = db.ViewerPassports.Add(
            new ViewerPassport
            {
                HostId = host.Id,
                TwitchUserId = "streamer-id",
                Login = "streamer",
                DisplayName = "Streamer",
                ProfileLine = profileLine,
                Visibility = visibility,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
