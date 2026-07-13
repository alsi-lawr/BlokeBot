using BlokeBot.Features.CustomCommands;
using BlokeBot.Hosting;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CustomCommandExecutionTests
{
    [Test]
    public async Task NormalizedHostAliasWithDisabledPeer_Dispatching_ExecutesOnlyEnabledHostCommand()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(dbFactory, hostId, "hello", ["Hello {channel} {command}"]);
        var disabledHostId = await SeedHostAsync(dbFactory, "disabled", HostFeatureFlags.Points);
        await SeedCommandAsync(dbFactory, disabledHostId, "hello", ["Hidden"]);
        await using var services = BuildServices(dbFactory);
        var dispatcher = services.GetRequiredService<TwitchCommandDispatcher>();
        List<string> replies = [];

        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "Streamer", "!HELLO"),
            RecordMessages(replies),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "Disabled", "!hello"),
            RecordMessages(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["Hello streamer hello"]);
    }

    [Test]
    public async Task ModeratorOnlyCommand_DispatchingByRoles_AllowsModeratorAndStreamerOnly()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(dbFactory, hostId, "secret", ["Hi {user}"], moderatorOnly: true);
        await using var services = BuildServices(dbFactory);
        var dispatcher = services.GetRequiredService<TwitchCommandDispatcher>();
        List<string> replies = [];

        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!secret"),
            RecordMessages(replies),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message(
                "moderator",
                "streamer",
                "!secret",
                new Dictionary<string, string> { ["mod"] = "1" }
            ),
            RecordMessages(replies),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("streamer", "streamer", "!secret"),
            RecordMessages(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["Hi moderator", "Hi streamer"]);
    }

    [Test]
    public async Task StandardAndUnknownTemplateTokens_Rendering_ReplacesKnownAndPreservesUnknown()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "echo",
            [
                "{user}|{channel}|{command}|{args}|{arg1}|{arg2}|{arg3}|{arg4}|{arg5}|{arg6}|{arg7}|{arg8}|{arg9}|{missing}",
            ],
            CustomMessageSelectionMode.First
        );
        await using var services = BuildServices(dbFactory);
        var dispatcher = services.GetRequiredService<TwitchCommandDispatcher>();
        List<string> replies = [];

        await dispatcher.DispatchResponsesAsync(
            Message("Viewer", "Streamer", "!echo one two three four five six seven eight nine ten"),
            RecordMessages(replies),
            CancellationToken.None
        );

        replies.ShouldBe([
            "viewer|streamer|echo|one two three four five six seven eight nine ten|one|two|three|four|five|six|seven|eight|nine|{missing}",
        ]);
    }

    [Test]
    public async Task ConfiguredMessageSelectionModes_Dispatching_UseExpectedVariantsAndRotation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "first",
            ["First A", "First B"],
            CustomMessageSelectionMode.First
        );
        var sequential = await SeedCommandAsync(
            dbFactory,
            hostId,
            "seq",
            ["Seq A", "Seq B"],
            CustomMessageSelectionMode.Sequential
        );
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "random",
            ["Random only"],
            CustomMessageSelectionMode.Random
        );
        await using var services = BuildServices(dbFactory);
        var dispatcher = services.GetRequiredService<TwitchCommandDispatcher>();
        List<string> replies = [];

        await DispatchMessageAsync(dispatcher, "viewer", "streamer", "!first", replies);
        await DispatchMessageAsync(dispatcher, "viewer", "streamer", "!first", replies);
        await DispatchMessageAsync(dispatcher, "viewer", "streamer", "!seq", replies);
        await DispatchMessageAsync(dispatcher, "viewer", "streamer", "!seq", replies);
        await DispatchMessageAsync(dispatcher, "viewer", "streamer", "!random", replies);

        replies.ShouldBe(["First A", "First A", "Seq A", "Seq B", "Random only"]);
        await using var db = await dbFactory.CreateDbContextAsync();
        var currentIndex = await db
            .CustomMessageLibraryEntries.Where(x => x.Id == sequential.MessageLibraryEntryId)
            .Select(x => x.CurrentVariantIndex)
            .SingleAsync(CancellationToken.None);
        currentIndex.ShouldBe(0);
    }

    [Test]
    public async Task CounterCommand_Dispatching_IncrementsAndRendersNewCount()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var seed = await SeedCommandAsync(
            dbFactory,
            hostId,
            "death",
            ["Count {count} {user}"],
            counterCommand: true,
            counterValue: 41
        );
        await using var services = BuildServices(dbFactory);
        var dispatcher = services.GetRequiredService<TwitchCommandDispatcher>();
        List<string> replies = [];

        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!death"),
            RecordMessages(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["Count 42 viewer"]);
        await using var db = await dbFactory.CreateDbContextAsync();
        var value = await db
            .CustomCounters.Where(x => x.Id == seed.CounterId)
            .Select(x => x.Value)
            .SingleAsync(CancellationToken.None);
        value.ShouldBe(42);
    }

    [Test]
    public async Task GlobalAndUserCooldowns_Dispatching_RespectScopeBoundaryAndMinimum()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "global",
            ["global {user}"],
            cooldownScope: CustomCommandCooldownScope.Global
        );
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "usercd",
            ["user {user}"],
            cooldownSeconds: 10,
            cooldownScope: CustomCommandCooldownScope.User
        );
        await using var services = BuildServices(dbFactory, minimumCooldownSeconds: 5, clock);
        var dispatcher = services.GetRequiredService<TwitchCommandDispatcher>();
        List<string> replies = [];

        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!global", replies);
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!global", replies);
        clock.Advance(TimeSpan.FromSeconds(5));
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!global", replies);
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!usercd", replies);
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!usercd", replies);
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!usercd", replies);
        clock.Advance(TimeSpan.FromSeconds(9));
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!usercd", replies);
        clock.Advance(TimeSpan.FromSeconds(1));
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!usercd", replies);

        replies.ShouldBe(["global alice", "global bob", "user alice", "user bob", "user alice"]);
    }

    private static ServiceProvider BuildServices(
        SqliteBlokeBotDbFactory dbFactory,
        int minimumCooldownSeconds = 0,
        TimeProvider? clock = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(dbFactory);
        services.AddSingleton(
            Options.Create(
                new BlokeBotOptions
                {
                    CustomCommands = new BlokeBotCustomCommandOptions
                    {
                        MinimumCooldownSeconds = minimumCooldownSeconds,
                    },
                }
            )
        );
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddBlokeBotCustomCommands(CustomAnnouncementDeliveryMode.Disabled);
        services.AddTwitchCommands().AddCommandModule<CustomCommandModule>();
        return services.BuildServiceProvider();
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login,
        HostFeatureFlags enabledFeatures = HostFeatureFlags.All
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            EnabledFeatures = enabledFeatures,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<CommandSeed> SeedCommandAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string alias,
        string[] variants,
        CustomMessageSelectionMode selectionMode = CustomMessageSelectionMode.Sequential,
        bool moderatorOnly = false,
        int cooldownSeconds = 0,
        CustomCommandCooldownScope cooldownScope = CustomCommandCooldownScope.Global,
        bool counterCommand = false,
        long? counterValue = null
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var entry = new CustomMessageLibraryEntry
        {
            HostId = hostId,
            Name = $"{alias}-message",
            SelectionMode = selectionMode,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Variants = variants
                .Select(
                    (text, index) => new CustomMessageVariant { SortOrder = index, Text = text }
                )
                .ToList(),
        };
        db.CustomMessageLibraryEntries.Add(entry);

        CustomCounter? counter = null;
        if (counterCommand)
        {
            counter = new CustomCounter
            {
                HostId = hostId,
                Name = $"{alias}-counter",
                Value = counterValue ?? 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.CustomCounters.Add(counter);
        }

        await db.SaveChangesAsync();
        var command = new CustomCommand
        {
            HostId = hostId,
            Name = $"{alias}-command",
            Enabled = true,
            ModeratorOnly = moderatorOnly,
            CooldownSeconds = cooldownSeconds,
            CooldownScope = cooldownScope,
            Action = counter is null
                ? new MessageCustomCommandAction
                {
                    HostId = hostId,
                    MessageLibraryEntryId = entry.Id,
                }
                : new CounterCustomCommandAction
                {
                    HostId = hostId,
                    MessageLibraryEntryId = entry.Id,
                    CounterId = counter.Id,
                },
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.CustomCommands.Add(command);
        await db.SaveChangesAsync();
        db.CustomCommandAliases.Add(
            new CustomCommandAlias
            {
                HostId = hostId,
                CustomCommandId = command.Id,
                Alias = CommandAliasNormalizer.Normalize(alias),
            }
        );
        await db.SaveChangesAsync();
        return new CommandSeed(command.Id, entry.Id, counter?.Id);
    }

    private static ChatMessage Message(
        string login,
        string channel,
        string text,
        IReadOnlyDictionary<string, string>? tags = null
    )
    {
        return new(
            login,
            channel,
            text,
            $":{login}!u@h PRIVMSG #{channel} :{text}",
            tags ?? new Dictionary<string, string>()
        );
    }

    private static async Task DispatchMessageAsync(
        TwitchCommandDispatcher dispatcher,
        string login,
        string channel,
        string text,
        List<string> replies
    )
    {
        await dispatcher.DispatchResponsesAsync(
            Message(login, channel, text),
            RecordMessages(replies),
            CancellationToken.None
        );
    }

    private static CommandResponder RecordMessages(List<string> replies)
    {
        return (response, _) =>
        {
            replies.Add(response.Message);
            return ValueTask.CompletedTask;
        };
    }

    private sealed record CommandSeed(int CommandId, int MessageLibraryEntryId, int? CounterId);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _current = now;

        public override DateTimeOffset GetUtcNow()
        {
            return _current;
        }

        public void Advance(TimeSpan interval)
        {
            _current += interval;
        }
    }
}
