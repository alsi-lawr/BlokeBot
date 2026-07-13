using BlokeBot.Commands;
using BlokeBot.Eventing;
using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class GuessingAliasTests
{
    [Test]
    public async Task SelectedGuessingProfile_LoadingConfiguration_ReturnsOnlyOwnedAliases()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);

        var config = await service.LoadConfigurationAsync(
            seed.Host.Id,
            seed.SpecialProfile.Id,
            CancellationToken.None
        );

        config.Aliases.StartAliases.ShouldBe("special");
        config.Aliases.GuessAliases.ShouldBeEmpty();
    }

    [Test]
    public async Task AliasUsedByAnotherProfile_SavingConfiguration_RejectsCollision()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);
        var config = await service.LoadConfigurationAsync(
            seed.Host.Id,
            seed.SpecialProfile.Id,
            CancellationToken.None
        );
        config.Aliases.StartAliases = "default";

        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(seed.Host.Id, config, CancellationToken.None)
        );
    }

    [Test]
    public async Task ProfileOwnedStartAlias_ExecutingWithoutArgument_StartsOwningProfile()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        List<string> replies = [];
        var strategy = new StartGuessingCommandStrategy(
            new GuessingCommandService(dbFactory),
            RoundService(dbFactory)
        );

        await strategy.ExecuteAsync(
            CommandContext(
                seed.Host.Login,
                "special",
                new AppCommandRouteState(seed.Host.Id, seed.SpecialProfile.Id),
                [],
                replies
            ),
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var round = await db.Rounds.SingleAsync(CancellationToken.None);
        round.GuessRoundProfileId.ShouldBe(seed.SpecialProfile.Id);
        replies.ShouldBe(["Started Special: blue"]);
    }

    [Test]
    public async Task ProfileWhisperDelivery_StartingOpenRound_ReturnsWhisperResponse()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Rounds.Add(
                new GuessRound
                {
                    HostId = seed.Host.Id,
                    GuessRoundProfileId = seed.SpecialProfile.Id,
                    Status = GuessRoundStatus.Open,
                    StartedAtUtc = DateTime.UtcNow,
                }
            );
            db.ReplyDeliverySettings.Add(
                new ReplyDeliverySetting
                {
                    HostId = seed.Host.Id,
                    Feature = ReplyFeature.Guessing,
                    ScopeId = seed.SpecialProfile.Id,
                    ReplyKey = GuessingReplyKeys.RoundAlreadyOpen,
                    Target = ReplyDeliveryTarget.Whisper,
                }
            );
            await db.SaveChangesAsync();
        }
        List<CommandResponse> responses = [];
        var strategy = new StartGuessingCommandStrategy(
            new GuessingCommandService(dbFactory),
            RoundService(dbFactory)
        );

        await strategy.ExecuteAsync(
            TypedCommandContext(
                seed.Host.Login,
                "special",
                new AppCommandRouteState(seed.Host.Id, seed.SpecialProfile.Id),
                [],
                responses
            ),
            CancellationToken.None
        );

        var response = responses.Single();
        response.Target.ShouldBe(CommandResponseTarget.Whisper);
        response.Message.ShouldBe("Already open.");
    }

    [Test]
    public async Task UnsupportedWhisperKey_StartingRound_ReturnsChatAnnouncement()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.ReplyDeliverySettings.Add(
                new ReplyDeliverySetting
                {
                    HostId = seed.Host.Id,
                    Feature = ReplyFeature.Guessing,
                    ScopeId = seed.SpecialProfile.Id,
                    ReplyKey = "round_started",
                    Target = ReplyDeliveryTarget.Whisper,
                }
            );
            await db.SaveChangesAsync();
        }
        List<CommandResponse> responses = [];
        var strategy = new StartGuessingCommandStrategy(
            new GuessingCommandService(dbFactory),
            RoundService(dbFactory)
        );

        await strategy.ExecuteAsync(
            TypedCommandContext(
                seed.Host.Login,
                "special",
                new AppCommandRouteState(seed.Host.Id, seed.SpecialProfile.Id),
                [],
                responses
            ),
            CancellationToken.None
        );

        var response = responses.Single();
        response.Target.ShouldBe(CommandResponseTarget.Chat);
        response.Message.ShouldBe("Started Special: blue");
    }

    [Test]
    public async Task ProfileWideWhisperAnswers_SavingConfiguration_UpdatesEveryOption()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);
        var config = await service.LoadConfigurationAsync(
            seed.Host.Id,
            seed.SpecialProfile.Id,
            CancellationToken.None
        );
        config.Profile.WhisperAnswerReplies = true;
        config.Profile.Options.Add(
            new GuessOptionEditor
            {
                Name = "green",
                ReplyText = "Green",
                ReplyTarget = ReplyDeliveryTarget.Chat,
            }
        );

        await service.SaveConfigurationAsync(seed.Host.Id, config, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var targets = await db
            .GuessOptions.Where(x => x.GuessRoundProfileId == seed.SpecialProfile.Id)
            .Select(x => x.ReplyTarget)
            .ToListAsync(CancellationToken.None);
        targets.Count.ShouldBe(2);
        targets.ShouldAllBe(x => x == ReplyDeliveryTarget.Whisper);
    }

    [Test]
    public async Task MixedLegacyOptionDelivery_RecordingGuess_UsesProfileWideTarget()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Rounds.Add(
                new GuessRound
                {
                    HostId = seed.Host.Id,
                    GuessRoundProfileId = seed.SpecialProfile.Id,
                    Status = GuessRoundStatus.Open,
                    StartedAtUtc = DateTime.UtcNow,
                }
            );
            db.GuessOptions.Add(
                new GuessOption
                {
                    GuessRoundProfileId = seed.SpecialProfile.Id,
                    Name = "green",
                    ReplyText = "Green",
                    ReplyTarget = ReplyDeliveryTarget.Whisper,
                }
            );
            await db.SaveChangesAsync();
        }
        var service = new GuessingVoteService(
            dbFactory,
            new GuessingChangeNotifier(TestEventBus.Create<AppEventKind>())
        );

        var result = await service.RecordGuessAsync(
            seed.Host.Login,
            "viewer",
            "blue",
            CancellationToken.None
        );

        result.Target.ShouldBe(CommandResponseTarget.Whisper);
        result.Message.ShouldBe("Blue");
    }

    private static GuessingConfigurationService ConfigurationService(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        return new(
            dbFactory,
            new CommandAliasRegistry(),
            new GuessingChangeNotifier(TestEventBus.Create<AppEventKind>())
        );
    }

    private static GuessingRoundService RoundService(SqliteBlokeBotDbFactory dbFactory)
    {
        return new(
            dbFactory,
            new GuessingChangeNotifier(TestEventBus.Create<AppEventKind>()),
            new PointBalanceService(dbFactory),
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>())
        );
    }

    private static CommandStrategyContext<GuessCommandKind, AppCommandRouteState> CommandContext(
        string channel,
        string commandName,
        AppCommandRouteState routeState,
        IReadOnlyList<string> args,
        List<string> replies
    )
    {
        var command = TestCommandContext.Create(
            "moderator",
            channel,
            commandName,
            args,
            (CommandResponse response, CancellationToken _) =>
            {
                replies.Add(response.Message);
                return ValueTask.CompletedTask;
            }
        );

        return new CommandStrategyContext<GuessCommandKind, AppCommandRouteState>(
            GuessCommandKind.Start,
            routeState,
            command,
            args
        );
    }

    private static CommandStrategyContext<
        GuessCommandKind,
        AppCommandRouteState
    > TypedCommandContext(
        string channel,
        string commandName,
        AppCommandRouteState routeState,
        IReadOnlyList<string> args,
        List<CommandResponse> responses
    )
    {
        var command = TestCommandContext.Create(
            "moderator",
            channel,
            commandName,
            args,
            (CommandResponse response, CancellationToken _) =>
            {
                responses.Add(response);
                return ValueTask.CompletedTask;
            }
        );

        return new CommandStrategyContext<GuessCommandKind, AppCommandRouteState>(
            GuessCommandKind.Start,
            routeState,
            command,
            args
        );
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
                RoundStartedReply = "Started {round}: {options}",
                RoundAlreadyOpenReply = "Already open.",
            },
            Options = [new GuessOption { Name = "red", ReplyText = "Red" }],
        };
        var specialProfile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Special",
            Slug = "special",
            ReplySettings = new BotReplySettings
            {
                RoundStartedReply = "Started {round}: {options}",
                RoundAlreadyOpenReply = "Already open.",
            },
            Options = [new GuessOption { Name = "blue", ReplyText = "Blue" }],
        };
        db.Profiles.AddRange(defaultProfile, specialProfile);
        await db.SaveChangesAsync();

        db.CommandAliases.AddRange(
            new CommandAlias
            {
                HostId = host.Id,
                GuessRoundProfileId = defaultProfile.Id,
                Kind = AppCommandKind.Start,
                Alias = "default",
            },
            new CommandAlias
            {
                HostId = host.Id,
                GuessRoundProfileId = specialProfile.Id,
                Kind = AppCommandKind.Start,
                Alias = "special",
            }
        );
        await db.SaveChangesAsync();
        return new ProfileSeed(host, defaultProfile, specialProfile);
    }

    private sealed record ProfileSeed(
        BotHost Host,
        GuessRoundProfile DefaultProfile,
        GuessRoundProfile SpecialProfile
    );
}
