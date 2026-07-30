using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class ViewerCommandCatalogTests
{
    [Test]
    public async Task ChannelState_LoadingCatalog_ListsViewerCanonicalRoutesOnceInStableOrder()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Live("stream"))
        );

        var snapshot = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);

        snapshot.Names.ShouldBe([
            "!choices",
            "!clip",
            "!commands",
            "!enter",
            "!give",
            "!join",
            "!leave",
            "!loyalty",
            "!moment",
            "!position",
            "!predict",
            "!queue",
            "!ready",
            "!request",
            "!requests",
            "!requestvote",
            "!wager",
            "!zeta",
        ]);
        snapshot.Names.ShouldNotContain("!alpha");
        snapshot.Names.ShouldNotContain("!secret");
        snapshot
            .Names.Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ShouldBe(snapshot.Names.Count);
        snapshot.Conflicts.ShouldBeEmpty();
    }

    [Test]
    public async Task ClosedRoundAndOffline_LoadingCatalog_UsesChannelWideAvailabilityOnly()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var round = await db.Rounds.SingleAsync(value => value.HostId == fixture.HostId);
            round.Status = GuessRoundStatus.Closed;
            round.ClosedAtUtc = DateTime.UtcNow;
            var giveaway = await db.PointsGiveaways.SingleAsync(value =>
                value.HostId == fixture.HostId
            );
            giveaway.Status = PointsGiveawayStatus.Completed;
            var board = await db.RequestBoards.SingleAsync(value => value.HostId == fixture.HostId);
            board.IsOpen = false;
            var queue = await db.PlayQueues.SingleAsync(value => value.HostId == fixture.HostId);
            queue.IsOpen = false;
            await db.SaveChangesAsync();
        }

        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline())
        );
        var snapshot = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);

        snapshot.Names.ShouldNotContain("!predict");
        snapshot.Names.ShouldNotContain("!choices");
        snapshot.Names.ShouldNotContain("!enter");
        snapshot.Names.ShouldNotContain("!request");
        snapshot.Names.ShouldNotContain("!join");
        snapshot.Names.ShouldNotContain("!moment");
        snapshot.Names.ShouldNotContain("!clip");
        snapshot.Names.ShouldContain("!requests");
        snapshot.Names.ShouldContain("!requestvote");
        snapshot.Names.ShouldContain("!queue");
        snapshot.Names.ShouldContain("!leave");
        snapshot.Names.ShouldContain("!position");
        snapshot.Names.ShouldContain("!ready");
    }

    [Test]
    public async Task LegacyFixedRouteShadow_LoadingCatalog_OmitsCanonicalAndReportsConflict()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var custom = await db
                .CustomCommands.Include(value => value.Aliases)
                .SingleAsync(value => value.Name == "Public");
            custom.Aliases.Clear();
            custom.Aliases.Add(
                new CustomCommandAlias
                {
                    HostId = fixture.HostId,
                    Alias = "join",
                    SortOrder = 0,
                }
            );
            await db.SaveChangesAsync();
        }

        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Live("stream"))
        );
        var snapshot = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);

        snapshot.Entries.ShouldNotContain(entry =>
            entry.Source == ViewerCommandCatalogSource.Custom && entry.Name == "!join"
        );
        snapshot.Conflicts.ShouldContain(message => message.Contains("!join"));
    }

    [Test]
    public async Task ConfiguredAlias_DispatchingPublicChat_ReturnsSharedCatalogSnapshot()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        var liveness = new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline());
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext>>(dbFactory);
        services.AddSingleton<IHostStreamLivenessProvider>(liveness);
        services.AddSingleton<ViewerCommandCatalogService>();
        services.AddChatCommands().AddCommandModule<ViewerCommandCatalogModule>();
        await using var provider = services.BuildServiceProvider();
        var expected = await provider
            .GetRequiredService<ViewerCommandCatalogService>()
            .LoadForHostAsync(fixture.HostId, CancellationToken.None);
        var responses = new List<CommandResponse>();

        await provider
            .GetRequiredService<ChatCommandDispatcher>()
            .DispatchResponsesAsync(
                new ChatMessage(
                    "viewer",
                    "streamer",
                    "!commands",
                    "raw",
                    new Dictionary<string, string>()
                ),
                (response, _) =>
                {
                    responses.Add(response);
                    return ValueTask.CompletedTask;
                },
                CancellationToken.None
            );

        responses
            .Single()
            .Message.ShouldBe($"Available viewer commands: {string.Join(", ", expected.Names)}.");
    }

    [Test]
    public async Task LongCatalog_DispatchingPublicChat_PersistsEveryOrderedPartWithoutTruncation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.CustomCommands.AddRange(
                Enumerable
                    .Range(0, 40)
                    .Select(index => new CustomCommand
                    {
                        HostId = fixture.HostId,
                        Name = $"Catalog {index:D2}",
                        Enabled = true,
                        Aliases =
                        [
                            new CustomCommandAlias
                            {
                                HostId = fixture.HostId,
                                Alias = $"catalog-command-{index:D2}",
                                SortOrder = 0,
                            },
                        ],
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow,
                    })
            );
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(dbFactory);
        services.AddSingleton<IHostStreamLivenessProvider>(
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline())
        );
        services.AddSingleton<ViewerCommandCatalogService>();
        services.AddChatCommands().AddCommandModule<ViewerCommandCatalogModule>();
        await using var provider = services.BuildServiceProvider();
        var snapshot = await provider
            .GetRequiredService<ViewerCommandCatalogService>()
            .LoadForHostAsync(fixture.HostId, CancellationToken.None);
        var expected = $"Available viewer commands: {string.Join(", ", snapshot.Names)}.";
        expected.Length.ShouldBeGreaterThan(500);

        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var queue = CreateQueue(
            outbox,
            new RecordingPublicChatTransport(),
            new ManualTestTimeProvider(Utc(12, 0, 0)),
            new BotOptions { MaxChatMessageLength = 100 }
        );
        var sender = new PublicChatCommandResponseSender(
            new PublicChatMessageSender(queue),
            NullLogger<PublicChatCommandResponseSender>.Instance
        );
        var source = new ChatMessage(
            "viewer",
            "streamer",
            "!commands",
            "raw",
            new Dictionary<string, string>()
        );

        await provider
            .GetRequiredService<ChatCommandDispatcher>()
            .DispatchResponsesAsync(
                source,
                (response, ct) => sender.SendAsync(source, response, ct),
                CancellationToken.None
            );

        await using var verify = await dbFactory.CreateDbContextAsync();
        var parts = await verify
            .PublicChatOutboxMessages.AsNoTracking()
            .OrderBy(value => value.Id)
            .Select(value => value.Message!)
            .ToArrayAsync();
        parts.Length.ShouldBeGreaterThan(1);
        parts.ShouldAllBe(part => part.Length <= 100);
        string.Join(" ", parts).ShouldBe(expected);
    }

    [Test]
    public async Task AuthorityCollisionAndBlank_SavingConfiguration_PreserveOwnedState()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        var service = new CommandsConfigurationService(
            dbFactory,
            TestEventBus.Create<AppEventKind>()
        );

        var unauthorized = await service.SaveAsync(
            Session(fixture.HostId, AuthRole.Moderator),
            fixture.HostId,
            new CommandsConfigurationSaveCommand("catalog"),
            CancellationToken.None
        );
        var staleHost = await service.SaveAsync(
            Session(fixture.HostId, AuthRole.Streamer),
            fixture.HostId + 1,
            new CommandsConfigurationSaveCommand("catalog"),
            CancellationToken.None
        );
        var collision = await service.SaveAsync(
            Session(fixture.HostId, AuthRole.Streamer),
            fixture.HostId,
            new CommandsConfigurationSaveCommand("join"),
            CancellationToken.None
        );
        var disabled = await service.SaveAsync(
            Session(fixture.HostId, AuthRole.Streamer),
            fixture.HostId,
            new CommandsConfigurationSaveCommand(" "),
            CancellationToken.None
        );

        unauthorized.ShouldBeOfType<CommandsConfigurationSaveOutcome.Unauthorized>();
        staleHost.ShouldBeOfType<CommandsConfigurationSaveOutcome.Unauthorized>();
        collision
            .ShouldBeOfType<CommandsConfigurationSaveOutcome.AliasConflict>()
            .Alias.ShouldBe("join");
        disabled.ShouldBeOfType<CommandsConfigurationSaveOutcome.Saved>();
        (await service.LoadAsync(fixture.HostId, CancellationToken.None)).ShouldBe(
            new CommandsConfiguration(string.Empty, null)
        );
    }

    private static async Task<CatalogFixture> SeedCatalogFixtureAsync(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CommandsAliasesConfigured = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var profile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        db.CommandAliases.AddRange(
            AppAlias(host.Id, AppCommandKind.Commands, "commands"),
            AppAlias(host.Id, AppCommandKind.Points, "loyalty"),
            AppAlias(host.Id, AppCommandKind.GivePoints, "give"),
            AppAlias(host.Id, AppCommandKind.Gamble, "wager"),
            AppAlias(host.Id, AppCommandKind.Join, "enter"),
            AppAlias(host.Id, AppCommandKind.AddPoints, "secretadd"),
            AppAlias(host.Id, profile.Id, AppCommandKind.Guess, "predict"),
            AppAlias(host.Id, profile.Id, AppCommandKind.Guesses, "choices"),
            AppAlias(host.Id, profile.Id, AppCommandKind.Start, "startpredict")
        );
        db.Rounds.Add(
            new GuessRound
            {
                HostId = host.Id,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Open,
                StartedAtUtc = DateTime.UtcNow,
            }
        );
        db.PointsGiveaways.Add(
            new PointsGiveaway
            {
                HostId = host.Id,
                Status = PointsGiveawayStatus.Active,
                StartedAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddMinutes(10),
            }
        );
        db.RequestBoards.Add(
            new RequestBoard
            {
                HostId = host.Id,
                Slug = "games",
                Title = "Games",
                IsOpen = true,
                VotingEnabled = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        db.PlayQueues.Add(
            new PlayQueue
            {
                HostId = host.Id,
                Slug = "main",
                Name = "Main",
                ActivityName = "Game",
                IsOpen = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        db.CustomCommands.AddRange(
            new CustomCommand
            {
                HostId = host.Id,
                Name = "Public",
                Enabled = true,
                Aliases =
                [
                    new CustomCommandAlias
                    {
                        HostId = host.Id,
                        Alias = "zeta",
                        SortOrder = 0,
                    },
                    new CustomCommandAlias
                    {
                        HostId = host.Id,
                        Alias = "alpha",
                        SortOrder = 1,
                    },
                ],
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            },
            new CustomCommand
            {
                HostId = host.Id,
                Name = "Moderator",
                Enabled = true,
                ModeratorOnly = true,
                Aliases =
                [
                    new CustomCommandAlias
                    {
                        HostId = host.Id,
                        Alias = "secret",
                        SortOrder = 0,
                    },
                ],
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
        return new(host.Id);
    }

    private static CommandAlias AppAlias(int hostId, AppCommandKind kind, string alias)
    {
        return AppAlias(hostId, null, kind, alias);
    }

    private static CommandAlias AppAlias(
        int hostId,
        int? profileId,
        AppCommandKind kind,
        string alias
    )
    {
        return new()
        {
            HostId = hostId,
            GuessRoundProfileId = profileId,
            Kind = kind,
            Alias = alias,
        };
    }

    private static AuthenticatedSession Session(int hostId, AuthRole role)
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", role);
        return new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = role == AuthRole.Streamer ? "streamer-id" : "moderator-id",
            Login = role == AuthRole.Streamer ? "streamer" : "moderator",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private sealed class StaticLivenessProvider(HostStreamLivenessOutcome outcome)
        : IHostStreamLivenessProvider
    {
        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin)
        {
            return IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(Result<HostStreamLivenessOutcome, Never>.Success(outcome))
            );
        }
    }

    private sealed record CatalogFixture(int HostId);
}
