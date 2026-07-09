using System.Reflection;
using BlokeBot.Commands;
using BlokeBot.Eventing;
using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class GuessingAliasTests
{
    [Test]
    public async Task Configuration_loads_aliases_for_selected_profile_only()
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
    public async Task Configuration_rejects_duplicate_aliases_across_profiles()
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
    public async Task Profile_owned_start_alias_starts_owning_profile_without_argument()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        List<string> replies = [];
        var strategy = new StartGuessingCommandStrategy(
            new GuessingCommandService(dbFactory),
            new GuessingRoundService(
                dbFactory,
                new GuessingChangeNotifier(new EventBus<AppEventKind>())
            )
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
    public async Task Profile_reply_delivery_controls_round_already_open_response_target()
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
                    Feature = ReplyDeliveryFeature.Guessing,
                    ScopeId = seed.SpecialProfile.Id,
                    ReplyKey = GuessingReplyKeys.RoundAlreadyOpen,
                    Target = ReplyDeliveryTargets.Whisper,
                }
            );
            await db.SaveChangesAsync();
        }
        List<TwitchCommandResponse> responses = [];
        var strategy = new StartGuessingCommandStrategy(
            new GuessingCommandService(dbFactory),
            new GuessingRoundService(
                dbFactory,
                new GuessingChangeNotifier(new EventBus<AppEventKind>())
            )
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
        response.Target.ShouldBe(TwitchCommandResponseTarget.Whisper);
        response.Message.ShouldBe("Already open.");
    }

    [Test]
    public async Task Profile_start_announcement_ignores_unsupported_whisper_delivery_key()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.ReplyDeliverySettings.Add(
                new ReplyDeliverySetting
                {
                    HostId = seed.Host.Id,
                    Feature = ReplyDeliveryFeature.Guessing,
                    ScopeId = seed.SpecialProfile.Id,
                    ReplyKey = "round_started",
                    Target = ReplyDeliveryTargets.Whisper,
                }
            );
            await db.SaveChangesAsync();
        }
        List<TwitchCommandResponse> responses = [];
        var strategy = new StartGuessingCommandStrategy(
            new GuessingCommandService(dbFactory),
            new GuessingRoundService(
                dbFactory,
                new GuessingChangeNotifier(new EventBus<AppEventKind>())
            )
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
        response.Target.ShouldBe(TwitchCommandResponseTarget.Chat);
        response.Message.ShouldBe("Started Special: blue");
    }

    private static GuessingConfigurationService ConfigurationService(
        SqliteBlokeBotDbFactory dbFactory
    ) =>
        new(
            dbFactory,
            new CommandAliasRegistry(),
            new GuessingChangeNotifier(new EventBus<AppEventKind>())
        );

    private static CommandStrategyContext<GuessCommandKind, AppCommandRouteState> CommandContext(
        string channel,
        string commandName,
        AppCommandRouteState routeState,
        IReadOnlyList<string> args,
        List<string> replies
    )
    {
        var constructor = typeof(TwitchCommandContext)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 4);
        var text = $"!{commandName}";
        var command = (TwitchCommandContext)
            constructor.Invoke([
                new TwitchChatMessage(
                    "moderator",
                    channel,
                    text,
                    $":moderator!u@h PRIVMSG #{channel} :{text}",
                    new Dictionary<string, string>()
                ),
                commandName,
                new EmptyServiceProvider(),
                new Func<string, CancellationToken, ValueTask>(
                    (message, _) =>
                    {
                        replies.Add(message);
                        return ValueTask.CompletedTask;
                    }
                ),
            ]);

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
        List<TwitchCommandResponse> responses
    )
    {
        var constructor = typeof(TwitchCommandContext)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 5);
        var text = $"!{commandName}";
        var command = (TwitchCommandContext)
            constructor.Invoke([
                new TwitchChatMessage(
                    "moderator",
                    channel,
                    text,
                    $":moderator!u@h PRIVMSG #{channel} :{text}",
                    new Dictionary<string, string>()
                ),
                commandName,
                new EmptyServiceProvider(),
                new Func<TwitchCommandResponse, CancellationToken, ValueTask>(
                    (response, _) =>
                    {
                        responses.Add(response);
                        return ValueTask.CompletedTask;
                    }
                ),
                false,
            ]);

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

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
