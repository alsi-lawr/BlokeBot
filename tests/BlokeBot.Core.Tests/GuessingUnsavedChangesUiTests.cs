using System.Globalization;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class GuessingUnsavedChangesUiTests
{
    [Test]
    public async Task NestedEdit_SwitchingAndKeepingEditing_PreservesCurrentDraft()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var context = CreateContext(dbFactory, seed.HostId);
        var page = context.Render<GuessingSettings>();
        page.Find("input[placeholder='answer']").Change("green");

        page.Find(RoundTypeChip(seed.SpecialProfileId)).Click();

        AssertSelectedProfile(page, seed.DefaultProfileId, "green");

        ChooseDialogAction(page, "Keep editing");

        page.FindAll("[data-unsaved-profile-dialog]").ShouldBeEmpty();
        AssertSelectedProfile(page, seed.DefaultProfileId, "green");
    }

    [Test]
    public async Task NestedEdit_SwitchingAndDiscarding_LoadsRequestedProfileWithoutSaving()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var context = CreateContext(dbFactory, seed.HostId);
        var page = context.Render<GuessingSettings>();
        page.Find("input[placeholder='answer']").Change("green");
        page.Find(RoundTypeChip(seed.SpecialProfileId)).Click();

        ChooseDialogAction(page, "Discard and switch");

        AssertSelectedProfile(page, seed.SpecialProfileId, "blue");
        await using var db = await dbFactory.CreateDbContextAsync();
        var persisted = await db
            .Profiles.Where(profile => profile.Id == seed.DefaultProfileId)
            .SelectMany(profile => profile.Options)
            .SingleAsync();
        persisted.Name.ShouldBe("red");
    }

    [Test]
    public async Task NestedEdit_SwitchingAndSaving_PersistsThenLoadsRequestedProfile()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var context = CreateContext(dbFactory, seed.HostId);
        var page = context.Render<GuessingSettings>();
        page.Find("input[placeholder='answer']").Change("green");
        page.Find(RoundTypeChip(seed.SpecialProfileId)).Click();

        ChooseDialogAction(page, "Save and switch");

        AssertSelectedProfile(page, seed.SpecialProfileId, "blue");
        await using var db = await dbFactory.CreateDbContextAsync();
        var persisted = await db
            .Profiles.Where(profile => profile.Id == seed.DefaultProfileId)
            .SelectMany(profile => profile.Options)
            .SingleAsync();
        persisted.Name.ShouldBe("green");
    }

    [Test]
    public async Task ConcurrentSaveFailure_SavingAndSwitching_PreservesCurrentTypeAndDraft()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var context = CreateContext(dbFactory, seed.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var page = context.Render<GuessingSettings>();
        page.Find("input[placeholder='answer']").Change("green");
        await using (var concurrentDb = await dbFactory.CreateDbContextAsync())
        {
            _ = await concurrentDb
                .Profiles.Where(profile => profile.Id == seed.DefaultProfileId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(
                        profile => profile.Revision,
                        profile => profile.Revision + 1
                    )
                );
        }
        page.Find(RoundTypeChip(seed.SpecialProfileId)).Click();

        ChooseDialogAction(page, "Save and switch");

        _ = page.Find("[data-unsaved-profile-dialog]");
        AssertSelectedProfile(page, seed.DefaultProfileId, "green");
        toasts.Current.ShouldHaveSingleItem().Kind.ShouldBe(ToastKind.Error);
        await using var db = await dbFactory.CreateDbContextAsync();
        var persisted = await db
            .Profiles.Where(profile => profile.Id == seed.DefaultProfileId)
            .SelectMany(profile => profile.Options)
            .SingleAsync();
        persisted.Name.ShouldBe("red");
    }

    private static void ChooseDialogAction(
        IRenderedComponent<GuessingSettings> page,
        string action
    ) =>
        page.FindAll("[data-unsaved-profile-dialog] button")
            .Single(button => button.TextContent.Trim() == action)
            .Click();

    private static void AssertSelectedProfile(
        IRenderedComponent<GuessingSettings> page,
        int profileId,
        string answer
    )
    {
        page.Find(RoundTypeChip(profileId)).GetAttribute("aria-pressed").ShouldBe("true");
        page.Find("input[placeholder='answer']").GetAttribute("value").ShouldBe(answer);
    }

    private static string RoundTypeChip(int profileId) =>
        $"#guessing-round-type-{profileId.ToString(CultureInfo.InvariantCulture)}";

    private static BunitContext CreateContext(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        var context = UiTestContextFactory.Create(dbFactory, hostId);
        _ = context.Services.AddSingleton<GuessingChangeNotifier>();
        _ = context.Services.AddSingleton<GuessingConfigurationService>();
        return context;
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
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
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
        _ = await db.SaveChangesAsync();
        return new(host.Id, defaultProfile.Id, specialProfile.Id);
    }

    private sealed record ProfileSeed(int HostId, int DefaultProfileId, int SpecialProfileId);
}
