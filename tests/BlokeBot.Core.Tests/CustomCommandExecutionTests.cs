using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosting;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

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
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
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
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
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
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];

        await dispatcher.DispatchResponsesAsync(
            Message("Viewer", "Streamer", "!echo one two"),
            RecordMessages(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["viewer|streamer|echo|one two|one|two||||||||{missing}"]);
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
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
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
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
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
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
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

    [Test]
    public async Task InvocationLimits_Dispatching_EnforceViewerAndTwitchStreamBoundaries()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "stream",
            ["stream {user}"],
            invocationLimit: CustomCommandInvocationLimit.OncePerStream
        );
        var userCommand = await SeedCommandAsync(
            dbFactory,
            hostId,
            "user",
            ["user {user}"],
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "both",
            ["both {user}"],
            invocationLimit: CustomCommandInvocationLimit.OncePerStreamPerUser
        );
        var streams = new MutableStreamLivenessProvider("stream-a");
        await using var services = BuildServices(dbFactory, streams: streams);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];

        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!stream", replies);
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!stream", replies);
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!user", replies);
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!user", replies);
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!user", replies);
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!both", replies);
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!both", replies);
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!both", replies);
        streams.StreamId = "stream-b";
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!stream", replies);
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!both", replies);
        await SetLimitAsync(
            dbFactory,
            userCommand.CommandId,
            CustomCommandInvocationLimit.Unlimited
        );
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!user", replies);
        await SetLimitAsync(
            dbFactory,
            userCommand.CommandId,
            CustomCommandInvocationLimit.OncePerUser
        );
        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!user", replies);

        replies.ShouldBe([
            "stream alice",
            "user alice",
            "user bob",
            "both alice",
            "both bob",
            "stream bob",
            "both alice",
            "user alice",
        ]);
        await using var db = await dbFactory.CreateDbContextAsync();
        (
            await db.CustomCommandInvocationClaims.CountAsync(claim =>
                claim.CustomCommandId == userCommand.CommandId && claim.TwitchUserId == "alice-id"
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task AccessAndCooldownRejections_PrecedeInvocationClaims()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "cooldown",
            ["{user}"],
            cooldownSeconds: 10,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "mod-only",
            ["reply"],
            moderatorOnly: true,
            cooldownSeconds: 30,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await using var services = BuildServices(dbFactory, clock: clock);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        var execution = services.GetRequiredService<CustomCommandExecutionService>();
        List<string> replies = [];

        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!cooldown", replies);
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!cooldown", replies);
        (
            await execution.ExecuteAsync(Context("alice", "!mod-only"), [], CancellationToken.None)
        ).ShouldBeOfType<CustomCommandExecutionOutcome.Handled>();
        (
            await execution.ExecuteAsync(
                Context(
                    "alice",
                    "!mod-only",
                    tags: new Dictionary<string, string> { ["user-id"] = "alice-id", ["mod"] = "1" }
                ),
                [],
                CancellationToken.None
            )
        ).ShouldBeOfType<CustomCommandExecutionOutcome.Handled>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(2);
            (
                await db.CustomCommandInvocationClaims.AnyAsync(claim =>
                    claim.TwitchUserId == "bob-id"
                )
            ).ShouldBeFalse();
        }

        clock.Advance(TimeSpan.FromSeconds(10));
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!cooldown", replies);

        replies.ShouldBe(["alice", "bob"]);
    }

    [Test]
    public async Task AcceptedClaim_WhenReplyDeliveryFails_RemainsConsumed()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "limited",
            ["reply"],
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await using var services = BuildServices(dbFactory);
        var execution = services.GetRequiredService<CustomCommandExecutionService>();
        var context = Context(
            "alice",
            "!limited",
            (_, _) => ValueTask.FromException(new IOException("delivery failed"))
        );

        await Should.ThrowAsync<IOException>(() =>
            execution.ExecuteAsync(context, [], CancellationToken.None).AsTask()
        );

        (
            await execution.ExecuteAsync(context, [], CancellationToken.None)
        ).ShouldBeOfType<CustomCommandExecutionOutcome.AlreadyUsed>();
    }

    [Test]
    public async Task StreamScopedLimit_WhenOfflineOrUnavailable_ReturnsExplicitOutcomeWithoutClaim()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "limited",
            ["reply"],
            invocationLimit: CustomCommandInvocationLimit.OncePerStream
        );
        var streams = new MutableStreamLivenessProvider(null);
        await using var services = BuildServices(dbFactory, streams: streams);
        var execution = services.GetRequiredService<CustomCommandExecutionService>();
        var context = Context("alice", "!limited");

        (
            await execution.ExecuteAsync(context, [], CancellationToken.None)
        ).ShouldBeOfType<CustomCommandExecutionOutcome.StreamOffline>();
        streams.Unavailable = true;
        (
            await execution.ExecuteAsync(context, [], CancellationToken.None)
        ).ShouldBeOfType<CustomCommandExecutionOutcome.StreamUnavailable>();

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Moderator_RetryingLimitedCommand_DoesNotBypassClaim()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "limited",
            ["reply"],
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await using var services = BuildServices(dbFactory);
        var execution = services.GetRequiredService<CustomCommandExecutionService>();

        (
            await execution.ExecuteAsync(Context("mod", "!limited"), [], CancellationToken.None)
        ).ShouldBeOfType<CustomCommandExecutionOutcome.Handled>();
        (
            await execution.ExecuteAsync(
                Context(
                    "mod",
                    "!limited",
                    tags: new Dictionary<string, string> { ["user-id"] = "mod-id", ["mod"] = "1" }
                ),
                [],
                CancellationToken.None
            )
        ).ShouldBeOfType<CustomCommandExecutionOutcome.AlreadyUsed>();
    }

    [Test]
    public async Task SimultaneousDatabaseBackedAttempts_ClaimExactlyOnce()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCommandAsync(
            dbFactory,
            hostId,
            "limited",
            ["reply"],
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await using var services = BuildServices(dbFactory);
        var execution = services.GetRequiredService<CustomCommandExecutionService>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = ExecuteAfterGateAsync();
        var second = ExecuteAfterGateAsync();
        gate.SetResult();

        var outcomes = await Task.WhenAll(first, second);

        outcomes.Count(outcome => outcome is CustomCommandExecutionOutcome.Handled).ShouldBe(1);
        outcomes.Count(outcome => outcome is CustomCommandExecutionOutcome.AlreadyUsed).ShouldBe(1);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);

        async Task<CustomCommandExecutionOutcome> ExecuteAfterGateAsync()
        {
            await gate.Task;
            return await execution.ExecuteAsync(
                Context("alice", "!limited"),
                [],
                CancellationToken.None
            );
        }
    }

    [Test]
    public async Task OverlayCueReplies_Dispatching_PreserveConfiguredOrderAndSuppressAfterReplyOnRejection()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedCueCommandAsync(dbFactory, hostId, "before", OverlayCueReplyOrder.Before);
        await SeedCueCommandAsync(dbFactory, hostId, "after", OverlayCueReplyOrder.After);
        await SeedCueCommandAsync(dbFactory, hostId, "disconnected", OverlayCueReplyOrder.After);
        await SeedCueCommandAsync(dbFactory, hostId, "rejected", OverlayCueReplyOrder.After);
        List<string> events = [];
        var admissions = new RecordingCueAdmissions(events);
        admissions.Outcomes.Enqueue(new OverlayCueAdmissionOutcome.Running(Guid.NewGuid()));
        admissions.Outcomes.Enqueue(new OverlayCueAdmissionOutcome.Queued(Guid.NewGuid()));
        admissions.Outcomes.Enqueue(
            new OverlayCueAdmissionOutcome.Disconnected(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddSeconds(30)
            )
        );
        admissions.Outcomes.Enqueue(new OverlayCueAdmissionOutcome.QueueRejected());
        await using var services = BuildServices(dbFactory, overlayCues: admissions);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();

        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!before"),
            RecordEvents(events),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!after"),
            RecordEvents(events),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!disconnected"),
            RecordEvents(events),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!rejected"),
            RecordEvents(events),
            CancellationToken.None
        );

        events.ShouldBe([
            "reply:before reply",
            "admit",
            "admit",
            "reply:after reply",
            "admit",
            "reply:disconnected reply",
            "admit",
        ]);
        admissions.ReferenceRequests.Count.ShouldBe(4);
    }

    [Test]
    public async Task OverlayCueParentsAndHostReferences_Dispatching_BlockBeforeCooldownClaimReplyAndAdmission()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var seed = await SeedCueCommandAsync(
            dbFactory,
            hostId,
            "cue",
            OverlayCueReplyOrder.After,
            cooldownSeconds: 60,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        var otherHostId = await SeedHostAsync(dbFactory, "other");
        var other = await SeedCueCommandAsync(
            dbFactory,
            otherHostId,
            "othercue",
            OverlayCueReplyOrder.After
        );
        var admissions = new RecordingCueAdmissions([]);
        admissions.ReferenceOutcomes.Enqueue(new OverlayCueReferenceOutcome.Available());
        admissions.ReferenceOutcomes.Enqueue(
            new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Target)
        );
        admissions.ReferenceOutcomes.Enqueue(
            new OverlayCueReferenceOutcome.Disabled(OverlayCueReferencePart.Cue)
        );
        admissions.Outcomes.Enqueue(new OverlayCueAdmissionOutcome.Running(Guid.NewGuid()));
        await using var services = BuildServices(dbFactory, overlayCues: admissions);
        var execution = services.GetRequiredService<CustomCommandExecutionService>();
        List<string> replies = [];

        await SetFeaturesAsync(dbFactory, hostId, HostFeatureFlags.Overlays);
        (
            await execution.ExecuteAsync(
                Context("viewer", "!cue", RecordMessages(replies)),
                [],
                CancellationToken.None
            )
        ).ShouldBeOfType<CustomCommandExecutionOutcome.Unhandled>();
        await SetFeaturesAsync(
            dbFactory,
            hostId,
            HostFeatureFlags.All & ~HostFeatureFlags.Overlays
        );
        var disabled = await execution.ExecuteAsync(
            Context("viewer", "!cue", RecordMessages(replies)),
            [],
            CancellationToken.None
        );
        disabled
            .ShouldBeOfType<CustomCommandExecutionOutcome.OverlayCue>()
            .Admission.ShouldBeOfType<OverlayCueAdmissionOutcome.ParentDisabledOrCancelled>();

        await SetFeaturesAsync(dbFactory, hostId, HostFeatureFlags.All);
        (
            await execution.ExecuteAsync(
                Context("viewer", "!cue", RecordMessages(replies)),
                [],
                CancellationToken.None
            )
        ).ShouldBeOfType<CustomCommandExecutionOutcome.OverlayCue>();

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var action = await db
                .CustomCommandActions.OfType<OverlayCueCustomCommandAction>()
                .SingleAsync(value => value.CustomCommandId == seed.CommandId);
            action.TargetOverlayPublicId = other.TargetOverlayId;
            action.CuePublicId = other.CueId;
            await db.SaveChangesAsync();
        }
        var crossHost = await execution.ExecuteAsync(
            Context("second", "!cue", RecordMessages(replies)),
            [],
            CancellationToken.None
        );

        crossHost
            .ShouldBeOfType<CustomCommandExecutionOutcome.OverlayCue>()
            .Admission.ShouldBeOfType<OverlayCueAdmissionOutcome.Missing>();

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var action = await db
                .CustomCommandActions.OfType<OverlayCueCustomCommandAction>()
                .SingleAsync(value => value.CustomCommandId == seed.CommandId);
            action.TargetOverlayPublicId = seed.TargetOverlayId;
            action.CuePublicId = seed.CueId;
            var cue = await db.OverlayCues.SingleAsync(value => value.PublicId == seed.CueId);
            cue.IsEnabled = false;
            await db.SaveChangesAsync();
        }
        var disabledReference = await execution.ExecuteAsync(
            Context("third", "!cue", RecordMessages(replies)),
            [],
            CancellationToken.None
        );
        disabledReference
            .ShouldBeOfType<CustomCommandExecutionOutcome.OverlayCue>()
            .Admission.ShouldBeOfType<OverlayCueAdmissionOutcome.Disabled>();
        admissions.Requests.Count.ShouldBe(1);
        admissions.ReferenceRequests.Count.ShouldBe(3);
        replies.ShouldBe(["cue reply"]);
        await using var assertionDb = await dbFactory.CreateDbContextAsync();
        (await assertionDb.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task OverlayCueOwnerTest_Executing_UsesAdmissionWithoutChatCooldownOrClaims()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var seed = await SeedCueCommandAsync(
            dbFactory,
            hostId,
            "cue",
            OverlayCueReplyOrder.Before,
            cooldownSeconds: 60,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        var admissions = new RecordingCueAdmissions([]);
        admissions.Outcomes.Enqueue(new OverlayCueAdmissionOutcome.Running(Guid.NewGuid()));
        await using var services = BuildServices(dbFactory, overlayCues: admissions);
        var execution = services.GetRequiredService<CustomCommandExecutionService>();

        var outcome = await execution.TestCueAsync(
            hostId,
            new OverlayCueCustomCommandActionEditor
            {
                TargetOverlayPublicId = seed.TargetOverlayId,
                CuePublicId = seed.CueId,
                QueuePolicy = OverlayCueQueuePolicy.Replace,
                ReplyOrder = OverlayCueReplyOrder.Before,
            },
            CancellationToken.None
        );

        outcome.ShouldBeOfType<OverlayCueAdmissionOutcome.Running>();
        admissions.ReferenceRequests.ShouldHaveSingleItem();
        admissions.Requests.Single().Origin.ShouldBe(OverlayCueAdmissionOrigin.OwnerTest);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
    }

    private static ServiceProvider BuildServices(
        SqliteBlokeBotDbFactory dbFactory,
        int minimumCooldownSeconds = 0,
        TimeProvider? clock = null,
        IHostStreamLivenessProvider? streams = null,
        IOverlayCueAdmissionService? overlayCues = null
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

        if (streams is not null)
        {
            services.AddSingleton(streams);
        }
        if (overlayCues is not null)
        {
            services.AddSingleton(overlayCues);
        }

        services.AddBlokeBotCustomCommands(CustomAnnouncementDeliveryMode.Disabled);
        services.AddChatCommands().AddCommandModule<CustomCommandModule>();
        return services.BuildServiceProvider();
    }

    private static async Task<CueCommandSeed> SeedCueCommandAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string alias,
        OverlayCueReplyOrder replyOrder,
        int cooldownSeconds = 0,
        CustomCommandInvocationLimit invocationLimit = CustomCommandInvocationLimit.Unlimited
    )
    {
        var command = await SeedCommandAsync(
            dbFactory,
            hostId,
            alias,
            [$"{alias} reply"],
            cooldownSeconds: cooldownSeconds,
            invocationLimit: invocationLimit
        );
        var targetId = Guid.NewGuid();
        var cueId = Guid.NewGuid();
        await using var db = await dbFactory.CreateDbContextAsync();
        db.OverlayInstances.Add(
            new OverlayInstance
            {
                HostId = hostId,
                PublicId = targetId,
                Name = $"{alias}-target",
                Type = OverlayType.CuePlayer,
                IsEnabled = true,
                ConfigurationJson = """{"schemaVersion":1}""",
                AccessKeyDigest = System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(alias)
                ),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        db.OverlayCues.Add(
            new OverlayCue
            {
                HostId = hostId,
                PublicId = cueId,
                Name = $"{alias}-cue",
                IsEnabled = true,
                DurationMilliseconds = 1000,
                QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                ConfigurationJson = """{"schemaVersion":1,"layers":[]}""",
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        var stored = await db
            .CustomCommands.Include(value => value.Action)
            .SingleAsync(value => value.Id == command.CommandId);
        db.CustomCommandActions.Remove(stored.Action);
        await db.SaveChangesAsync();
        stored.Action = new OverlayCueCustomCommandAction
        {
            HostId = hostId,
            TargetOverlayPublicId = targetId,
            CuePublicId = cueId,
            QueuePolicy = OverlayCueQueuePolicy.Enqueue,
            ReplyOrder = replyOrder,
            ZeroArgumentMessageLibraryEntryId = command.MessageLibraryEntryId,
        };
        await db.SaveChangesAsync();
        return new(command.CommandId, targetId, cueId);
    }

    private static async Task SetFeaturesAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        HostFeatureFlags features
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = features;
        await db.SaveChangesAsync();
    }

    private static CommandResponder RecordEvents(ICollection<string> events) =>
        (response, _) =>
        {
            events.Add($"reply:{response.Message}");
            return ValueTask.CompletedTask;
        };

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
        long? counterValue = null,
        CustomCommandInvocationLimit invocationLimit = CustomCommandInvocationLimit.Unlimited
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
            InvocationLimit = invocationLimit,
            Action = counter is null
                ? new MessageCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = entry.Id,
                    OneArgumentMessageLibraryEntryId = entry.Id,
                    TwoArgumentMessageLibraryEntryId = entry.Id,
                }
                : new CounterCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = entry.Id,
                    OneArgumentMessageLibraryEntryId = entry.Id,
                    TwoArgumentMessageLibraryEntryId = entry.Id,
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
    ) =>
        new(
            login,
            channel,
            text,
            $":{login}!u@h PRIVMSG #{channel} :{text}",
            tags
                ?? new Dictionary<string, string> { ["user-id"] = $"{login.ToLowerInvariant()}-id" }
        );

    private static ChatCommandContext Context(
        string login,
        string text,
        CommandResponder? responder = null,
        IReadOnlyDictionary<string, string>? tags = null
    ) =>
        new()
        {
            Message = Message(login, "streamer", text, tags),
            CommandName = text.TrimStart('!'),
            Responder = responder ?? ((_, _) => ValueTask.CompletedTask),
        };

    private static async Task SetLimitAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int commandId,
        CustomCommandInvocationLimit limit
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var command = await db.CustomCommands.SingleAsync(stored => stored.Id == commandId);
        command.InvocationLimit = limit;
        await db.SaveChangesAsync();
    }

    private static async Task DispatchMessageAsync(
        ChatCommandDispatcher dispatcher,
        string login,
        string channel,
        string text,
        List<string> replies
    ) =>
        await dispatcher.DispatchResponsesAsync(
            Message(login, channel, text),
            RecordMessages(replies),
            CancellationToken.None
        );

    private static CommandResponder RecordMessages(List<string> replies) =>
        (response, _) =>
        {
            replies.Add(response.Message);
            return ValueTask.CompletedTask;
        };

    private sealed record CommandSeed(int CommandId, int MessageLibraryEntryId, int? CounterId);

    private sealed record CueCommandSeed(int CommandId, Guid TargetOverlayId, Guid CueId);

    private sealed class RecordingCueAdmissions(ICollection<string> events)
        : IOverlayCueAdmissionService
    {
        public Queue<OverlayCueReferenceOutcome> ReferenceOutcomes { get; } = new();

        public List<OverlayCueReferenceRequest> ReferenceRequests { get; } = [];

        public Queue<OverlayCueAdmissionOutcome> Outcomes { get; } = new();

        public List<OverlayCueAdmissionRequest> Requests { get; } = [];

        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        )
        {
            ReferenceRequests.Add(request);
            return Task.FromResult(
                ReferenceOutcomes.TryDequeue(out var outcome)
                    ? outcome
                    : new OverlayCueReferenceOutcome.Available()
            );
        }

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(new OverlayCueAdmissionCatalog([], []));

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            events.Add("admit");
            return Task.FromResult(Outcomes.Dequeue());
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _current = now;

        public override DateTimeOffset GetUtcNow() => _current;

        public void Advance(TimeSpan interval) => _current += interval;
    }

    private sealed class MutableStreamLivenessProvider(string? streamId)
        : IHostStreamLivenessProvider
    {
        public string? StreamId { get; set; } = streamId;

        public bool Unavailable { get; set; }

        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin)
        {
            HostStreamLivenessOutcome outcome =
                Unavailable
                    ? new HostStreamLivenessOutcome.Unavailable(
                        HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                        new HttpRequestException("unavailable")
                    )
                : StreamId is { } current ? new HostStreamLivenessOutcome.Live(current)
                : new HostStreamLivenessOutcome.Offline();
            return IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(Result<HostStreamLivenessOutcome, Never>.Success(outcome))
            );
        }
    }
}
