using BlokeBot.Eventing;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.UI.Tests;

public sealed class GuessingSettingsLoadTests
{
    [Test]
    public async Task DeletedSelectedProfile_Selecting_ReloadsDefaultEditorWithTypedFeedback()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var context = CreateContext(dbFactory, seed.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var page = context.Render<GuessingSettings>();
        await DeleteProfilesAsync(dbFactory, seed.SpecialProfileId);

        page.Find("#profileSelect").Change(seed.SpecialProfileId.ToString());

        page.WaitForAssertion(() =>
        {
            page.Find("#profileSelect")
                .GetAttribute("value")
                .ShouldBe(seed.DefaultProfileId.ToString());
            page.Find("input[placeholder='Round type name']")
                .GetAttribute("value")
                .ShouldBe("Default");
        });
        var toast = toasts.Current.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.Warning);
        toast.Message.ShouldBe(
            "That round type is no longer available. Reloaded the current settings."
        );
    }

    [Test]
    public async Task DeletedSelectedAndDefaultProfiles_Selecting_LeavesNoEditorWithoutThrowing()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var context = CreateContext(dbFactory, seed.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var page = context.Render<GuessingSettings>();
        await DeleteProfilesAsync(dbFactory, seed.DefaultProfileId, seed.SpecialProfileId);

        page.Find("#profileSelect").Change(seed.SpecialProfileId.ToString());

        page.WaitForAssertion(() => page.Markup.ShouldContain("Loading guessing settings..."));
        toasts.Current.Select(toast => toast.Kind).ShouldBe([ToastKind.Warning, ToastKind.Error]);
        toasts.Current.ShouldAllBe(toast =>
            toast.Message
            == "That round type is no longer available. Reloaded the current settings."
        );
    }

    private static BunitContext CreateContext(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        var context = UiTestContextFactory.Create(dbFactory, hostId);
        context.Services.AddSingleton<GuessingChangeNotifier>();
        context.Services.AddSingleton<GuessingConfigurationService>();
        return context;
    }

    private static async Task DeleteProfilesAsync(
        SqliteBlokeBotDbFactory dbFactory,
        params int[] profileIds
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profiles = await db
            .Profiles.Where(profile => profileIds.Contains(profile.Id))
            .ToListAsync();
        db.Profiles.RemoveRange(profiles);
        await db.SaveChangesAsync();
    }

    private static async Task<ProfileSeed> SeedProfilesAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.Guessing,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var defaultProfile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
            ReplySettings = new BotReplySettings(),
            Options = [new GuessOption { Name = "red", ReplyText = "Red" }],
        };
        var specialProfile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Special",
            Slug = "special",
            ReplySettings = new BotReplySettings(),
            Options = [new GuessOption { Name = "blue", ReplyText = "Blue" }],
        };
        db.Profiles.AddRange(defaultProfile, specialProfile);
        await db.SaveChangesAsync();
        return new(host.Id, defaultProfile.Id, specialProfile.Id);
    }

    private sealed record ProfileSeed(int HostId, int DefaultProfileId, int SpecialProfileId);
}
