using BlokeBot.Commands;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class GuessingAliasTests
{
    [Test]
    public async Task SelectedGuessingProfile_LoadingConfiguration_ReturnsOnlyOwnedAliases()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);

        var config = await LoadConfigurationAsync(service, seed.Host.Id, seed.SpecialProfile.Id);

        config.Aliases.StartAliases.ShouldBe("special");
        config.Aliases.GuessAliases.ShouldBeEmpty();
    }

    [Test]
    public async Task LoadedAliasesAndOptions_MutatingBeforeSave_IsIsolatedThenPersistsOnSave()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);
        var editor = await LoadConfigurationAsync(service, seed.Host.Id, seed.SpecialProfile.Id);
        editor.Aliases.StartAliases = "updated";
        var editedOption = editor.Profile.Options.Single();
        editedOption.Name = "green";
        editedOption.ReplyText = "Green";
        editor.Profile.Options.Add(
            new GuessOptionEditor
            {
                Name = "amber",
                ReplyText = "Amber",
                ReplyTarget = ReplyDeliveryTarget.Chat,
            }
        );

        var command = ValidCommand(editor);
        var beforeSave = await LoadConfigurationAsync(
            service,
            seed.Host.Id,
            seed.SpecialProfile.Id
        );

        beforeSave.Aliases.StartAliases.ShouldBe("special");
        var unchangedOption = beforeSave.Profile.Options.ShouldHaveSingleItem();
        unchangedOption.Name.ShouldBe("blue");
        unchangedOption.ReplyText.ShouldBe("Blue");

        editor.Aliases.StartAliases = "later";
        editedOption.Name = "later";
        editedOption.ReplyText = "Later";
        editor.Profile.Options.Clear();
        await service.SaveConfiguration(seed.Host.Id, command).ExecuteAsync(CancellationToken.None);

        var afterSave = await LoadConfigurationAsync(service, seed.Host.Id, seed.SpecialProfile.Id);
        afterSave.Aliases.StartAliases.ShouldBe("updated");
        afterSave.Profile.Options.Select(option => option.Name).ShouldBe(["green", "amber"]);
        afterSave.Profile.Options.Select(option => option.ReplyText).ShouldBe(["Green", "Amber"]);
    }

    [Test]
    public async Task AliasUsedByAnotherProfile_SavingConfiguration_RejectsCollision()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);
        var config = await LoadConfigurationAsync(service, seed.Host.Id, seed.SpecialProfile.Id);
        config.Aliases.StartAliases = "default";

        var result = await service
            .SaveConfiguration(seed.Host.Id, ValidCommand(config))
            .ExecuteAsync(CancellationToken.None);

        result
            .Match<GuessingConfigurationSaveFailure?>(_ => null, failure => failure)
            .ShouldBeOfType<GuessingConfigurationSaveFailure.AliasAlreadyUsed>();
    }

    [Test]
    public async Task AliasUsedByCustomCommand_SavingConfiguration_RejectsWithoutMutation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await SeedCustomCommandAliasAsync(dbFactory, seed.Host.Id, "shared");
        var service = ConfigurationService(dbFactory);
        var config = await LoadConfigurationAsync(service, seed.Host.Id, seed.SpecialProfile.Id);
        config.Aliases.StartAliases = "shared";

        var result = await service
            .SaveConfiguration(seed.Host.Id, ValidCommand(config))
            .ExecuteAsync(CancellationToken.None);

        result
            .Match<GuessingConfigurationSaveFailure?>(_ => null, failure => failure)
            .ShouldBe(new GuessingConfigurationSaveFailure.AliasAlreadyUsed("shared"));
        await using var db = await dbFactory.CreateDbContextAsync();
        var aliases = await db
            .CommandAliases.OrderBy(alias => alias.Alias)
            .Select(alias => alias.Alias)
            .ToArrayAsync();
        aliases.ShouldBe(["default", "special"]);
        (await db.CustomCommandAliases.SingleAsync()).Alias.ShouldBe("shared");
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
                new AppCommandRouteState.GuessingProfile(seed.Host.Id, seed.SpecialProfile.Id),
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
                new AppCommandRouteState.GuessingProfile(seed.Host.Id, seed.SpecialProfile.Id),
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
                new AppCommandRouteState.GuessingProfile(seed.Host.Id, seed.SpecialProfile.Id),
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
        var config = await LoadConfigurationAsync(service, seed.Host.Id, seed.SpecialProfile.Id);
        config.Profile.WhisperAnswerReplies = true;
        config.Profile.Options.Add(
            new GuessOptionEditor
            {
                Name = "green",
                ReplyText = "Green",
                ReplyTarget = ReplyDeliveryTarget.Chat,
            }
        );

        await service
            .SaveConfiguration(seed.Host.Id, ValidCommand(config))
            .ExecuteAsync(CancellationToken.None);

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

        var result = await service
            .RecordGuess(seed.Host.Login, "viewer", "blue")
            .RunAsync(CancellationToken.None);

        result.ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        result.Target.ShouldBe(CommandResponseTarget.Whisper);
        result.Message.ShouldBe("Blue");
    }

    private static GuessingConfigurationService ConfigurationService(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        return new(dbFactory, new GuessingChangeNotifier(TestEventBus.Create<AppEventKind>()));
    }

    private static async Task<GuessingConfiguration> LoadConfigurationAsync(
        GuessingConfigurationService service,
        int hostId,
        int profileId
    )
    {
        var result = await service
            .LoadConfiguration(hostId, new GuessingProfileSelection.Selected(profileId))
            .ExecuteAsync(CancellationToken.None);
        return result.Match(
            configuration => configuration,
            failure => throw new InvalidOperationException(failure.Message)
        );
    }

    private static GuessingConfigurationSaveCommand ValidCommand(GuessingConfiguration draft)
    {
        return GuessingConfigurationValidator
            .Validate(draft)
            .Match(
                command => command,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(error => error.Message))
                    )
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

    private static async Task SeedCustomCommandAliasAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string alias
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        db.CustomCommands.Add(
            new CustomCommand
            {
                HostId = hostId,
                Name = "Existing custom command",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Aliases = [new CustomCommandAlias { HostId = hostId, Alias = alias }],
            }
        );
        await db.SaveChangesAsync();
    }

    private sealed record ProfileSeed(
        BotHost Host,
        GuessRoundProfile DefaultProfile,
        GuessRoundProfile SpecialProfile
    );
}
