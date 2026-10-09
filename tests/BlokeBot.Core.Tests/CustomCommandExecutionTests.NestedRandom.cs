using System.Globalization;
using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CustomCommandExecutionTests
{
    [Test]
    [Arguments("{random_between|1|{arg1}}", "!roll 10", 1, 10)]
    [Arguments("{random_from|{random_between|{arg1}|{arg2}}}", "!roll -2 2", -2, 2)]
    [Arguments("{random_between|{random_from|{arg1}}|{random_from|{arg2}}}", "!roll -2 2", -2, 2)]
    [Arguments("{random_between|{arg1}|10}", "!roll -4", -4, 10)]
    [Arguments("{random_between|{arg1}|{arg2}}", "!roll -2 +2", -2, 2)]
    [Arguments("{random_between|{arg1}|{arg2}}", "!roll -5 -5", -5, -5)]
    [Arguments(
        "{random_between|{arg1}|{arg2}}",
        "!roll -2147483648 2147483647",
        int.MinValue,
        int.MaxValue
    )]
    [Arguments(
        "{random_between|{arg1}|{arg2}}",
        "!roll -2147483648 -2147483648",
        int.MinValue,
        int.MinValue
    )]
    [Arguments(
        "{random_between|{arg1}|{arg2}}",
        "!roll 2147483647 2147483647",
        int.MaxValue,
        int.MaxValue
    )]
    public async Task NestedBounds_SavingThenDispatching_UsesRealArgumentsAndInclusiveIntegerBounds(
        string template,
        string invocation,
        int minimum,
        int maximum
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "streamer");
        await SaveReplyConfigurationAsync(database, hostId, ReplyConfiguration(template));
        var random = new BoundRecordingRandomSource();
        await using var services = BuildServices(database, random: random);
        List<string> replies = [];

        await services
            .GetRequiredService<ChatCommandDispatcher>()
            .DispatchResponsesAsync(
                Message("viewer", "streamer", invocation),
                RecordChatReplies(replies),
                CancellationToken.None
            );

        replies.ShouldBe([maximum.ToString(CultureInfo.InvariantCulture)]);
        random.Bounds.ShouldBe([(minimum, maximum)]);
    }

    [Test]
    [Arguments("!roll", "missing")]
    [Arguments("!roll 1.5", "whole")]
    [Arguments("!roll private-value", "whole")]
    [Arguments("!roll 2147483648", "2147483647")]
    [Arguments("!roll -2147483649", "2147483647")]
    [Arguments("!roll 0", "lower")]
    [Arguments("!roll {random_between|1|1}", "whole")]
    public async Task InvalidNestedBounds_SavedCommand_SendsOnlyCauseOrientedReplacement(
        string invocation,
        string cause
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "streamer");
        await SaveReplyConfigurationAsync(
            database,
            hostId,
            ReplyConfiguration(
                "private-prefix {random_from|{random_between|1|{arg1}}} private-suffix"
            )
        );
        var random = new BoundRecordingRandomSource();
        await using var services = BuildServices(database, random: random);
        List<string> replies = [];

        await services
            .GetRequiredService<ChatCommandDispatcher>()
            .DispatchResponsesAsync(
                Message("viewer", "streamer", invocation),
                RecordChatReplies(replies),
                CancellationToken.None
            );

        var reply = replies.ShouldHaveSingleItem();
        reply.ShouldContain(cause);
        reply.ShouldNotContain("private-");
        reply.ShouldNotContain("{random_");
        random.Bounds.ShouldBeEmpty();
    }

    [Test]
    public async Task NestedCounterReply_RenderFailure_KeepsCommittedCountClaimRotationAndCooldown()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "streamer");
        var draft = ReplyConfiguration(
            "{random_from|{user}:{channel}:{command}:{args}:{arg9}:{count}:{unknown}} {random_between|{count}|{arg1}}"
        );
        draft.MessageEntries.Single().SelectionMode = CustomMessageSelectionMode.Sequential;
        draft.MessageEntries.Single().Variants.Add(new() { Id = -5, Text = "next" });
        draft.Counters.Add(
            new()
            {
                Id = -4,
                Name = "Roll count",
                Value = 41,
            }
        );
        var command = draft.Commands.Single();
        var routes = ((MessageCustomCommandActionEditor)command.Action).ReplyRoutes;
        command.Action = new CounterCustomCommandActionEditor
        {
            CounterId = -4,
            ReplyRoutes = routes,
        };
        command.CooldownSeconds = 10;
        command.InvocationLimit = CustomCommandInvocationLimit.OncePerUser;
        await SaveReplyConfigurationAsync(database, hostId, draft);
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        List<string> effects = [];
        var random = new BoundRecordingRandomSource(() => effects.Add("render"));
        var observer = new CounterEffectObserver(effects);
        await using var services = BuildServices(
            database,
            clock: clock,
            random: random,
            counterObserver: observer
        );
        List<string> replies = [];
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();

        await dispatcher.DispatchResponsesAsync(
            Message(
                "viewer",
                "streamer",
                "!roll 0",
                new Dictionary<string, string>
                {
                    ["user-id"] = "viewer-id",
                    ["id"] = "invocation-id",
                }
            ),
            RecordChatReplies(replies),
            CancellationToken.None
        );
        effects.ShouldBe(["counter:42", "render"]);
        replies.ShouldHaveSingleItem().ShouldContain("lower");
        var execution = services.GetRequiredService<CustomCommandExecutionService>();
        _ = (
            await execution.ExecuteAsync(
                Context("viewer", "!roll", RecordMessages(replies)),
                ["100"],
                CancellationToken.None
            )
        ).ShouldBeOfType<CustomCommandExecutionOutcome.Cooldown>();
        clock.Advance(TimeSpan.FromSeconds(10));
        _ = (
            await execution.ExecuteAsync(
                Context("viewer", "!roll", RecordMessages(replies)),
                ["100"],
                CancellationToken.None
            )
        ).ShouldBeOfType<CustomCommandExecutionOutcome.AlreadyUsed>();

        replies.Count.ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomCounters.SingleAsync()).Value.ShouldBe(42);
        (await verify.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
        (await verify.CustomMessageLibraryEntries.SingleAsync()).CurrentVariantIndex.ShouldBe(1);
        random.Bounds.ShouldBeEmpty();
    }

    [Test]
    public async Task NestedContextAndCounter_SavedCommand_RendersCommittedValuesAsOpaqueData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "streamer");
        var draft = ReplyConfiguration(
            "{random_from|{user}:{channel}:{command}:{args}:{arg9}:{count}:{unknown}} {random_between|{count}|{count}}"
        );
        draft.Counters.Add(
            new()
            {
                Id = -4,
                Name = "Count",
                Value = 41,
            }
        );
        draft.Commands.Single().Action = new CounterCustomCommandActionEditor
        {
            CounterId = -4,
            ReplyRoutes = (
                (MessageCustomCommandActionEditor)draft.Commands.Single().Action
            ).ReplyRoutes,
        };
        await SaveReplyConfigurationAsync(database, hostId, draft);
        var random = new BoundRecordingRandomSource();
        await using var services = BuildServices(database, random: random);
        List<string> replies = [];

        await DispatchMessageAsync(
            services.GetRequiredService<ChatCommandDispatcher>(),
            "viewer",
            "streamer",
            "!roll one|{random_between|1|1}",
            replies
        );

        replies.ShouldBe(["viewer:streamer:roll:one|{random_between|1|1}::42:{unknown} 42"]);
        random.Bounds.ShouldBe([(42, 42)]);
    }

    [Test]
    public async Task InvalidNestedCueReply_Dispatching_PreservesBeforeAfterAndRejectedAdmissionOwnership()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "streamer");
        _ = await SeedCueCommandAsync(database, hostId, "before", OverlayCueReplyOrder.Before);
        _ = await SeedCueCommandAsync(database, hostId, "after", OverlayCueReplyOrder.After);
        _ = await SeedCueCommandAsync(database, hostId, "rejected", OverlayCueReplyOrder.After);
        await using (var configure = await database.CreateDbContextAsync())
        {
            foreach (var variant in await configure.CustomMessageVariants.ToListAsync())
            {
                variant.Text = "{random_from|{random_between|1|{arg1}}}";
            }
            _ = await configure.SaveChangesAsync();
        }
        List<string> events = [];
        var admissions = new RecordingCueAdmissions(events);
        admissions.Outcomes.Enqueue(new OverlayCueAdmissionOutcome.Running(Guid.NewGuid()));
        admissions.Outcomes.Enqueue(new OverlayCueAdmissionOutcome.Queued(Guid.NewGuid()));
        admissions.Outcomes.Enqueue(new OverlayCueAdmissionOutcome.QueueRejected());
        await using var services = BuildServices(database, overlayCues: admissions);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();

        foreach (var alias in new[] { "before", "after", "rejected" })
        {
            await dispatcher.DispatchResponsesAsync(
                Message("viewer", "streamer", $"!{alias}"),
                (response, _) =>
                {
                    response.Message.ShouldContain("missing");
                    events.Add("reply");
                    return ValueTask.CompletedTask;
                },
                CancellationToken.None
            );
        }

        events.ShouldBe(["reply", "admit", "admit", "reply", "admit"]);
        admissions.Requests.Count.ShouldBe(3);
    }

    [Test]
    public async Task NestedViewer_UnauthorizedCommand_DoesNotReadChattersOrCommitEffects()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "streamer");
        _ = await SeedCommandAsync(
            database,
            hostId,
            "roll",
            ["{random_from|{random_viewer}}"],
            allowEveryone: false,
            counterCommand: true,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        var chatters = new CountingChatterSource();
        await using var services = BuildServices(database, chatters: chatters);
        List<string> replies = [];

        await DispatchMessageAsync(
            services.GetRequiredService<ChatCommandDispatcher>(),
            "viewer",
            "streamer",
            "!roll",
            replies
        );

        replies.ShouldBeEmpty();
        chatters.CallCount.ShouldBe(0);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomCounters.SingleAsync()).Value.ShouldBe(0);
        (await verify.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
    }

    [Test]
    [Arguments("{random_between|1|{arg1}")]
    [Arguments("{random_between|1|{arg1}}|{random_from|{random_between|1|2}")]
    [Arguments("{random_between|{arg1}|2|3}")]
    [Arguments("{random_between|{arg1}|not-a-number}")]
    [Arguments("{random_from|{random_between|2|1}}")]
    [Arguments("{random_from|{random_viewer|extra}}")]
    [Arguments("{random_from|{arg1}| }")]
    public void MalformedAuthoredNestedReply_ConfigurationValidation_IdentifiesVariantError(
        string template
    )
    {
        var errors = CustomCommandConfigurationValidator
            .Validate(ReplyConfiguration(template))
            .Match(
                static _ => Array.Empty<CustomCommandConfigurationValidationError>(),
                static invalid => invalid
            );
        errors.ShouldNotBeEmpty();
        errors
            .Any(static error =>
                error.Target.FieldKind == CustomCommandValidationFieldKind.VariantText
            )
            .ShouldBeTrue();
    }

    private static CustomCommandConfiguration ReplyConfiguration(string template) =>
        new()
        {
            TimeZoneId = "UTC",
            MessageEntries =
            [
                new()
                {
                    Id = -1,
                    Name = "Roll reply",
                    Variants = [new() { Id = -2, Text = template }],
                },
            ],
            Commands =
            [
                new()
                {
                    Id = -3,
                    Name = "Roll",
                    Aliases = "roll",
                    AllowEveryone = true,
                    Action = new MessageCustomCommandActionEditor
                    {
                        ReplyRoutes = new()
                        {
                            ZeroArgumentMessageLibraryEntryId = -1,
                            OneArgumentMessageLibraryEntryId = -1,
                            TwoArgumentMessageLibraryEntryId = -1,
                        },
                    },
                },
            ],
        };

    private static async Task SaveReplyConfigurationAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        CustomCommandConfiguration draft
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        var service = new CustomCommandConfigurationService(
            database,
            new CustomCommandAliasRegistry(),
            new CustomCommandConfigurationGraphWriter(
                database,
                new UnavailableOverlayCueAdmissionService(),
                TimeProvider.System
            ),
            new HostCustomCommandSettingsService(database, events),
            new ReplyTestAnnouncementReadiness(),
            events,
            TimeProvider.System
        );
        var command = CustomCommandConfigurationValidator
            .Validate(draft)
            .Match(
                static valid => valid,
                errors =>
                    throw new ShouldAssertException(
                        string.Join("; ", errors.Select(static error => error.Message))
                    )
            );
        var saved = await service
            .SaveConfiguration(hostId, command)
            .ExecuteAsync(CancellationToken.None);
        _ = saved.Match(
            static _ => true,
            failure => throw new ShouldAssertException(failure.Message)
        );
        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        loaded
            .MessageEntries.Single()
            .Variants.Select(static variant => variant.Text)
            .ShouldBe(
                draft.MessageEntries.Single().Variants.Select(static variant => variant.Text)
            );
    }

    private sealed class ReplyTestAnnouncementReadiness : ITwitchAnnouncementReadinessProvider
    {
        public Task<TwitchAnnouncementReadiness> GetReadinessAsync(
            string channelLogin,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new TwitchAnnouncementReadiness(
                    TwitchAnnouncementAvailability.Unavailable,
                    string.Empty
                )
            );
    }

    private static CommandResponder RecordChatReplies(List<string> replies) =>
        (response, _) =>
        {
            response.Target.ShouldBe(CommandResponseTarget.Chat);
            replies.Add(response.Message);
            return ValueTask.CompletedTask;
        };

    private sealed class CounterEffectObserver(List<string> effects) : IBingoCounterEventSink
    {
        public Task CounterChangedAsync(
            int hostId,
            string invocationId,
            int counterId,
            string counterName,
            long value,
            BingoViewer? viewer,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken
        )
        {
            effects.Add($"counter:{value}");
            return Task.CompletedTask;
        }
    }

    private sealed class BoundRecordingRandomSource(Action? onSelection = null)
        : IMessageLibraryRandomSource
    {
        public List<(int Minimum, int Maximum)> Bounds { get; } = [];

        public int Next(int exclusiveMaximum)
        {
            onSelection?.Invoke();
            return 0;
        }

        public int NextInclusive(int minimum, int maximum)
        {
            Bounds.Add((minimum, maximum));
            return maximum;
        }
    }
}
