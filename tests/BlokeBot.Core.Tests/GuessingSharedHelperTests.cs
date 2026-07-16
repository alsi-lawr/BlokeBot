using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class GuessingSharedHelperTests
{
    [Test]
    public void MissingAvailableGuessesReply_MappingToEditor_UsesDefaultReply()
    {
        var editor = ReplySettingsMapper.ToEditor(new BotReplySettings());

        editor.AvailableGuessesReply.ShouldBe(GuessingDefaults.Replies().AvailableGuessesReply);
    }

    [Test]
    public async Task ProfileQueries_ExecutingOnSqlite_PreserveShapesFilteringAndOrdering()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var db = await dbFactory.CreateDbContextAsync();

        var basic = await db.Profiles.LoadProfileAsync(
            seed.HostId,
            seed.SpecialProfileId,
            CancellationToken.None
        );
        var withOptions = await db.Profiles.LoadProfileWithOptionsAsync(
            seed.HostId,
            seed.SpecialProfileId,
            CancellationToken.None
        );
        var defaultBasic = await db.Profiles.LoadDefaultProfileAsync(
            seed.HostId,
            CancellationToken.None
        );
        var defaultWithOptions = await db.Profiles.LoadDefaultProfileWithOptionsAsync(
            seed.HostId,
            CancellationToken.None
        );
        var byName = await db.Profiles.LoadProfileIdByNameAsync(
            seed.HostId,
            "SPECIAL",
            CancellationToken.None
        );
        var missingBasic = await db.Profiles.LoadProfileAsync(
            seed.HostId,
            int.MaxValue,
            CancellationToken.None
        );
        var missingWithOptions = await db.Profiles.LoadProfileWithOptionsAsync(
            seed.HostId,
            int.MaxValue,
            CancellationToken.None
        );
        var wrongHost = await db.Profiles.LoadProfileAsync(
            seed.HostId + 1,
            seed.SpecialProfileId,
            CancellationToken.None
        );

        basic.ShouldNotBeNull();
        basic.Id.ShouldBe(seed.SpecialProfileId);
        basic.Name.ShouldBe("Special");
        basic.Settings.NoOpenRoundReply.ShouldBe("special");
        withOptions.ShouldNotBeNull();
        withOptions.OptionNames.ShouldBe(["alpha", "zulu"]);
        defaultBasic.Id.ShouldBe(seed.DefaultProfileId);
        defaultWithOptions.Id.ShouldBe(seed.DefaultProfileId);
        defaultWithOptions.OptionNames.ShouldBe(["default"]);
        byName.ShouldBe(seed.SpecialProfileId);
        missingBasic.ShouldBeNull();
        missingWithOptions.ShouldBeNull();
        wrongHost.ShouldBeNull();
        db.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Test]
    public async Task ProfileQueries_CancelingExecution_PropagatesCancellation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var db = await dbFactory.CreateDbContextAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            db.Profiles.LoadProfileAsync(seed.HostId, seed.SpecialProfileId, cancellation.Token)
        );
        await Should.ThrowAsync<OperationCanceledException>(() =>
            db.Profiles.LoadProfileWithOptionsAsync(
                seed.HostId,
                seed.SpecialProfileId,
                cancellation.Token
            )
        );
        await Should.ThrowAsync<OperationCanceledException>(() =>
            GuessingReplySettingsQueries.LoadForProfileAsync(
                db,
                seed.HostId,
                seed.SpecialProfileId,
                cancellation.Token
            )
        );
    }

    [Test]
    public async Task ExplicitReplySettingsSources_Loading_PreserveSourceAndFallbackSemantics()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var db = await dbFactory.CreateDbContextAsync();

        var round = await GuessingReplySettingsQueries.LoadForRoundAsync(
            db,
            seed.HostId,
            seed.SpecialProfileId,
            CancellationToken.None
        );
        var profile = await GuessingReplySettingsQueries.LoadForProfileAsync(
            db,
            seed.HostId,
            seed.SpecialProfileId,
            CancellationToken.None
        );
        var defaultProfile = await GuessingReplySettingsQueries.LoadForDefaultAsync(
            db,
            seed.HostId,
            CancellationToken.None
        );
        var missingSettings = await GuessingReplySettingsQueries.LoadForProfileAsync(
            db,
            seed.HostId,
            seed.MissingSettingsProfileId,
            CancellationToken.None
        );
        var missingProfile = await GuessingReplySettingsQueries.LoadForProfileAsync(
            db,
            seed.HostId,
            int.MaxValue,
            CancellationToken.None
        );

        round.ProfileId.ShouldBe(seed.SpecialProfileId);
        round.Settings.NoOpenRoundReply.ShouldBe("special");
        round
            .ReplyDelivery.TargetFor(GuessingReplyKeys.NoOpenRound)
            .ShouldBe(CommandResponseTarget.Whisper);
        profile.ProfileId.ShouldBe(seed.SpecialProfileId);
        profile.Settings.NoOpenRoundReply.ShouldBe("special");
        defaultProfile.ProfileId.ShouldBe(seed.DefaultProfileId);
        defaultProfile.Settings.NoOpenRoundReply.ShouldBe("default");
        missingSettings.ProfileId.ShouldBe(seed.MissingSettingsProfileId);
        missingSettings.Settings.NoOpenRoundReply.ShouldBe(
            GuessingDefaults.Replies().NoOpenRoundReply
        );
        missingProfile.ProfileId.ShouldBe(int.MaxValue);
        missingProfile.Settings.NoOpenRoundReply.ShouldBe(
            GuessingDefaults.Replies().NoOpenRoundReply
        );
    }

    [Test]
    public async Task ActiveRoundAndExplicitProfile_RequestingReply_PrefersRoundProfile()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Rounds.Add(
                new GuessRound
                {
                    HostId = seed.HostId,
                    GuessRoundProfileId = seed.SpecialProfileId,
                    Status = GuessRoundStatus.Open,
                    StartedAtUtc = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        var response = await new GuessingCommandService(dbFactory).ModeratorOnlyResponseAsync(
            seed.HostLogin,
            new AppCommandRouteState.GuessingProfile(seed.HostId, seed.DefaultProfileId),
            CancellationToken.None
        );

        response.Message.ShouldBe("special moderator");
    }

    private static async Task<ProfileSeed> SeedProfilesAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
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
            ReplySettings = new BotReplySettings
            {
                NoOpenRoundReply = "default",
                ModeratorOnlyReply = "default moderator",
            },
            Options = [new GuessOption { Name = "default", ReplyText = "Default" }],
        };
        var specialProfile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Special",
            Slug = "special",
            ReplySettings = new BotReplySettings
            {
                NoOpenRoundReply = "special",
                ModeratorOnlyReply = "special moderator",
            },
            Options =
            [
                new GuessOption { Name = "zulu", ReplyText = "Zulu" },
                new GuessOption { Name = "alpha", ReplyText = "Alpha" },
            ],
        };
        var missingSettingsProfile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Fallback",
            Slug = "fallback",
        };
        db.Profiles.AddRange(defaultProfile, specialProfile, missingSettingsProfile);
        await db.SaveChangesAsync();

        db.ReplyDeliverySettings.Add(
            new ReplyDeliverySetting
            {
                HostId = host.Id,
                Feature = ReplyFeature.Guessing,
                ScopeId = specialProfile.Id,
                ReplyKey = GuessingReplyKeys.NoOpenRound,
                Target = ReplyDeliveryTarget.Whisper,
            }
        );
        await db.SaveChangesAsync();

        return new ProfileSeed(
            host.Id,
            host.Login,
            defaultProfile.Id,
            specialProfile.Id,
            missingSettingsProfile.Id
        );
    }

    private sealed record ProfileSeed(
        int HostId,
        string HostLogin,
        int DefaultProfileId,
        int SpecialProfileId,
        int MissingSettingsProfileId
    );
}
