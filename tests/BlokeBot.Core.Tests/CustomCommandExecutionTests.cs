using System.Collections.Immutable;
using System.Data.Common;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosting;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        _ = await SeedCommandAsync(dbFactory, hostId, "hello", ["Hello {channel} {command}"]);
        var disabledHostId = await SeedHostAsync(dbFactory, "disabled", HostFeatureFlags.Points);
        _ = await SeedCommandAsync(dbFactory, disabledHostId, "hello", ["Hidden"]);
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
    public async Task ModeratorGrant_DispatchingByRoles_AllowsModeratorAndStreamerOnly()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        _ = await SeedCommandAsync(
            dbFactory,
            hostId,
            "secret",
            ["Hi {user}"],
            allowEveryone: false,
            allowModerators: true
        );
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
    public async Task ComposableAccess_Dispatching_UsesStableSelectedIdAndIndependentGrants()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        _ = await SeedCommandAsync(
            dbFactory,
            hostId,
            "trusted",
            ["Hi {user}"],
            allowEveryone: false,
            allowModerators: true,
            allowedUsers: [new("selected-id", "old_login", "Old name")]
        );
        await using var services = BuildServices(dbFactory);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];

        await dispatcher.DispatchResponsesAsync(
            Message(
                "old_login",
                "streamer",
                "!trusted",
                new Dictionary<string, string> { ["user-id"] = "different-id" }
            ),
            RecordMessages(replies),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("old_login", "streamer", "!trusted", new Dictionary<string, string>()),
            RecordMessages(replies),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message(
                "renamed_login",
                "streamer",
                "!trusted",
                new Dictionary<string, string> { ["user-id"] = "selected-id" }
            ),
            RecordMessages(replies),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message(
                "moderator",
                "streamer",
                "!trusted",
                new Dictionary<string, string> { ["mod"] = "1", ["user-id"] = "moderator-id" }
            ),
            RecordMessages(replies),
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("streamer", "streamer", "!trusted", new Dictionary<string, string>()),
            RecordMessages(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["Hi renamed_login", "Hi moderator", "Hi streamer"]);
    }

    [Test]
    public async Task UnauthorizedSelectedUserCommand_Dispatching_MutatesNothingBeforeAuthorizedUse()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var seed = await SeedCommandAsync(
            dbFactory,
            hostId,
            "trusted-counter",
            ["Count {count}"],
            allowEveryone: false,
            allowedUsers: [new("selected-id", "viewer", "Viewer")],
            cooldownSeconds: 30,
            counterCommand: true,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await using var services = BuildServices(dbFactory);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];

        await dispatcher.DispatchResponsesAsync(
            Message(
                "viewer",
                "streamer",
                "!trusted-counter",
                new Dictionary<string, string> { ["user-id"] = "wrong-id" }
            ),
            RecordMessages(replies),
            CancellationToken.None
        );
        await using (var denied = await dbFactory.CreateDbContextAsync())
        {
            (
                await denied
                    .CustomCounters.Where(counter => counter.Id == seed.CounterId)
                    .Select(counter => counter.Value)
                    .SingleAsync()
            ).ShouldBe(0);
            (await denied.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
        }

        await dispatcher.DispatchResponsesAsync(
            Message(
                "renamed",
                "streamer",
                "!trusted-counter",
                new Dictionary<string, string> { ["user-id"] = "selected-id" }
            ),
            RecordMessages(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["Count 1"]);
        await using var accepted = await dbFactory.CreateDbContextAsync();
        (
            await accepted
                .CustomCounters.Where(counter => counter.Id == seed.CounterId)
                .Select(counter => counter.Value)
                .SingleAsync()
        ).ShouldBe(1);
        (await accepted.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task UnauthorizedOverlayCueCommand_Dispatching_DoesNotResolveOrAdmitProviderWork()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var seed = await SeedCueCommandAsync(
            dbFactory,
            hostId,
            "private-cue",
            OverlayCueReplyOrder.Before,
            cooldownSeconds: 30,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await using (var restrict = await dbFactory.CreateDbContextAsync())
        {
            var command = await restrict.CustomCommands.SingleAsync(value =>
                value.Id == seed.CommandId
            );
            command.AllowEveryone = false;
            _ = await restrict.SaveChangesAsync();
        }
        List<string> events = [];
        var admissions = new RecordingCueAdmissions(events);
        await using var services = BuildServices(dbFactory, overlayCues: admissions);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();

        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!private-cue"),
            RecordEvents(events),
            CancellationToken.None
        );

        events.ShouldBeEmpty();
        admissions.ReferenceRequests.ShouldBeEmpty();
        admissions.Requests.ShouldBeEmpty();
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task StandardAndUnknownTemplateTokens_Rendering_ReplacesKnownAndPreservesUnknown()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        _ = await SeedCommandAsync(
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
    public async Task RandomReply_Dispatching_RendersAfterVariantSelection()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        _ = await SeedCommandAsync(
            dbFactory,
            hostId,
            "choose",
            ["{random_from|first|second}"],
            CustomMessageSelectionMode.First
        );
        await using var services = BuildServices(
            dbFactory,
            random: new FixedRandomSource(index: 1)
        );
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];

        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!choose"),
            RecordMessages(replies),
            CancellationToken.None
        );

        replies.ShouldBe(["second"]);
    }

    [Test]
    public async Task FeatureOff_RandomViewerCommand_DoesNotReadChatters()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", HostFeatureFlags.Points);
        _ = await SeedCommandAsync(dbFactory, hostId, "choose", ["{random_viewer}"]);
        var chatters = new CountingChatterSource();
        await using var services = BuildServices(dbFactory, chatters: chatters);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();

        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!choose"),
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );

        chatters.CallCount.ShouldBe(0);
    }

    [Test]
    public async Task ConfiguredMessageSelectionModes_Dispatching_UseExpectedVariantsAndRotation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        _ = await SeedCommandAsync(
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
        _ = await SeedCommandAsync(
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
        _ = await SeedCommandAsync(
            dbFactory,
            hostId,
            "global",
            ["global {user}"],
            cooldownScope: CustomCommandCooldownScope.Global
        );
        _ = await SeedCommandAsync(
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
        _ = await SeedCommandAsync(
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
        _ = await SeedCommandAsync(
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
        _ = await SeedCommandAsync(
            dbFactory,
            hostId,
            "cooldown",
            ["{user}"],
            cooldownSeconds: 10,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        _ = await SeedCommandAsync(
            dbFactory,
            hostId,
            "mod-only",
            ["reply"],
            allowEveryone: false,
            allowModerators: true,
            cooldownSeconds: 30,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await using var services = BuildServices(dbFactory, clock: clock);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        var execution = services.GetRequiredService<CustomCommandExecutionService>();
        List<string> replies = [];

        await DispatchMessageAsync(dispatcher, "alice", "streamer", "!cooldown", replies);
        await DispatchMessageAsync(dispatcher, "bob", "streamer", "!cooldown", replies);
        _ = (
            await execution.ExecuteAsync(Context("alice", "!mod-only"), [], CancellationToken.None)
        ).ShouldBeOfType<CustomCommandExecutionOutcome.Handled>();
        _ = (
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
                await db.CustomCommandInvocationClaims.AnyAsync(static claim =>
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
        _ = await SeedCommandAsync(
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

        _ = await Should.ThrowAsync<IOException>(() =>
            execution.ExecuteAsync(context, [], CancellationToken.None).AsTask()
        );

        _ = (
            await execution.ExecuteAsync(context, [], CancellationToken.None)
        ).ShouldBeOfType<CustomCommandExecutionOutcome.AlreadyUsed>();
    }

    [Test]
    public async Task StreamScopedLimit_WhenOfflineOrUnavailable_ReturnsExplicitOutcomeWithoutClaim()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        _ = await SeedCommandAsync(
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

        _ = (
            await execution.ExecuteAsync(context, [], CancellationToken.None)
        ).ShouldBeOfType<CustomCommandExecutionOutcome.StreamOffline>();
        streams.Unavailable = true;
        _ = (
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
        _ = await SeedCommandAsync(
            dbFactory,
            hostId,
            "limited",
            ["reply"],
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await using var services = BuildServices(dbFactory);
        var execution = services.GetRequiredService<CustomCommandExecutionService>();

        _ = (
            await execution.ExecuteAsync(Context("mod", "!limited"), [], CancellationToken.None)
        ).ShouldBeOfType<CustomCommandExecutionOutcome.Handled>();
        _ = (
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
        _ = await SeedCommandAsync(
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
        _ = await SeedCueCommandAsync(dbFactory, hostId, "before", OverlayCueReplyOrder.Before);
        _ = await SeedCueCommandAsync(dbFactory, hostId, "after", OverlayCueReplyOrder.After);
        _ = await SeedCueCommandAsync(
            dbFactory,
            hostId,
            "disconnected",
            OverlayCueReplyOrder.After
        );
        _ = await SeedCueCommandAsync(dbFactory, hostId, "rejected", OverlayCueReplyOrder.After);
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
        _ = (
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
        _ = disabled
            .ShouldBeOfType<CustomCommandExecutionOutcome.OverlayCue>()
            .Admission.ShouldBeOfType<OverlayCueAdmissionOutcome.ParentDisabledOrCancelled>();

        await SetFeaturesAsync(dbFactory, hostId, HostFeatureFlags.All);
        _ = (
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
            _ = await db.SaveChangesAsync();
        }
        var crossHost = await execution.ExecuteAsync(
            Context("second", "!cue", RecordMessages(replies)),
            [],
            CancellationToken.None
        );

        _ = crossHost
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
            _ = await db.SaveChangesAsync();
        }
        var disabledReference = await execution.ExecuteAsync(
            Context("third", "!cue", RecordMessages(replies)),
            [],
            CancellationToken.None
        );
        _ = disabledReference
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

        _ = outcome.ShouldBeOfType<OverlayCueAdmissionOutcome.Running>();
        _ = admissions.ReferenceRequests.ShouldHaveSingleItem();
        admissions.Requests.Single().Origin.ShouldBe(OverlayCueAdmissionOrigin.OwnerTest);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
    }

    [Test]
    [Arguments(true, false, false, "viewer", "viewer-id", false, true)]
    [Arguments(false, true, false, "moderator", "moderator-id", true, true)]
    [Arguments(false, false, true, "renamed", "selected-id", false, true)]
    [Arguments(false, false, true, "viewer", "different-id", false, false)]
    [Arguments(false, false, false, "streamer", "streamer-id", false, true)]
    [Arguments(false, false, false, "viewer", "viewer-id", true, false)]
    public async Task AutomationAccess_Dispatching_UsesTheSharedComposablePolicyBeforeRuntime(
        bool allowEveryone,
        bool allowModerators,
        bool selectViewer,
        string login,
        string twitchUserId,
        bool moderator,
        bool expected
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        _ = await SeedAutomationCommandAsync(
            dbFactory,
            hostId,
            "automate",
            allowEveryone,
            allowModerators,
            selectViewer ? [new("selected-id", "old-login", "Selected")] : []
        );
        var runtime = new RecordingAutomationRuntime();
        await using var services = BuildServices(dbFactory, automations: runtime);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        var tags = new Dictionary<string, string> { ["user-id"] = twitchUserId };
        if (moderator)
        {
            tags["mod"] = "1";
        }

        await dispatcher.DispatchResponsesAsync(
            Message(login, "streamer", "!automate", tags),
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );

        runtime.Triggers.Count.ShouldBe(expected ? 1 : 0);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task AutomationContext_Dispatching_BoundsUntrustedDataAndUsesStableMessageIdentity()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero));
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var command = await SeedAutomationCommandAsync(
            dbFactory,
            hostId,
            "automate",
            allowEveryone: true
        );
        var runtime = new RecordingAutomationRuntime();
        var streams = new MutableStreamLivenessProvider("stream-id");
        await using var services = BuildServices(
            dbFactory,
            clock: clock,
            streams: streams,
            automations: runtime
        );
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        var messageId = "8be7e908-6372-4a35-8977-fda2b247e284";
        var arguments = Enumerable
            .Range(0, CustomCommandAutomationContext.MaximumArgumentCount + 10)
            .Select(index => $"argument-{index:D2}-" + new string('x', 20))
            .ToArray();
        var text = $"!AUTOMATE   {string.Join(' ', arguments)}";
        var tags = new Dictionary<string, string>
        {
            ["user-id"] = "viewer-id",
            ["display-name"] = "Viewer Name",
            ["subscriber"] = "1",
            ["mod"] = "1",
            ["id"] = messageId,
            ["tmi-sent-ts"] = "1785837300000",
        };

        await dispatcher.DispatchResponsesAsync(
            Message("Viewer", "Streamer", text, tags),
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );
        await dispatcher.DispatchResponsesAsync(
            Message("Viewer", "Streamer", text, tags),
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );
        _ = tags.Remove("id");
        await dispatcher.DispatchResponsesAsync(
            Message("Viewer", "Streamer", text, tags),
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );

        runtime.Triggers.Count.ShouldBe(3);
        var context = runtime.Triggers[0].Context;
        context.HostId.Value.ShouldBe(hostId);
        context.Channel.TwitchChannelId.ShouldBe("streamer-id");
        context.Channel.Login.ShouldBe("streamer");
        context.Actor.ShouldBe(new AutomationActor("viewer-id", "Viewer", "Viewer Name"));
        context.Stream.ShouldBe(new AutomationStream("stream-id", null, null, null));
        context.Arguments.Length.ShouldBe(CustomCommandAutomationContext.MaximumArgumentCount);
        context.Arguments.ShouldAllBe(argument =>
            argument.Value.Length <= CustomCommandAutomationContext.MaximumArgumentLength
        );
        context.Timestamps.OccurredAtUtc.ShouldBe(
            DateTimeOffset.FromUnixTimeMilliseconds(1785837300000)
        );
        context.Timestamps.ReceivedAtUtc.ShouldBe(clock.GetUtcNow());
        var variables = context.Variables.ForExecution();
        variables[new("command_id")].Value.ShouldBe(new AutomationValue.Number(command.CommandId));
        variables[new("command_name")].Value.ShouldBe(new AutomationValue.Text("automate-command"));
        variables[new("command_alias")].Value.ShouldBe(new AutomationValue.Text("automate"));
        variables[new("raw_arguments")]
            .Value.ShouldBeOfType<AutomationValue.Text>()
            .Value.Length.ShouldBe(CustomCommandAutomationContext.MaximumRawArgumentsLength);
        variables[new("raw_arguments")].Sensitivity.ShouldBe(AutomationDataSensitivity.Sensitive);
        variables[new("viewer_is_moderator")].Value.ShouldBe(new AutomationValue.Boolean(true));
        variables[new("viewer_is_subscriber")].Value.ShouldBe(new AutomationValue.Boolean(true));
        variables[new("twitch_message_id")].Value.ShouldBe(new AutomationValue.Text(messageId));
        runtime.Triggers[1].Context.Event.OccurrenceId.ShouldBe(context.Event.OccurrenceId);
        runtime.Triggers[2].Context.Event.OccurrenceId.ShouldNotBe(context.Event.OccurrenceId);
        foreach (var trigger in runtime.Triggers)
        {
            trigger.SourceConfiguration.ShouldBe(
                new CustomCommandSourceConfiguration(new(command.CommandId))
            );
        }
    }

    [Test]
    public async Task AutomationAcceptance_Enabled_CommitsCooldownClaimRunAndEffect()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero));
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var command = await SeedAutomationCommandAsync(
            dbFactory,
            hostId,
            "limited",
            allowEveryone: true,
            cooldownSeconds: 30,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        var chat = new RecordingPublicChatSender();
        await using var services = BuildServices(
            dbFactory,
            clock: clock,
            publicChat: chat,
            realAutomations: true
        );
        await SeedAutomationFlowAsync(services, hostId, command.CommandId);
        var execution = services.GetRequiredService<CustomCommandExecutionService>();
        var context = Context(
            "viewer",
            "!limited",
            tags: new Dictionary<string, string> { ["user-id"] = "viewer-id" }
        );

        var accepted = await execution.ExecuteAsync(context, [], CancellationToken.None);
        var blocked = await execution.ExecuteAsync(context, [], CancellationToken.None);

        accepted
            .ShouldBeOfType<CustomCommandExecutionOutcome.Automation>()
            .Dispatch.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        _ = blocked.ShouldBeOfType<CustomCommandExecutionOutcome.Cooldown>();
        services.GetRequiredService<CustomCommandCooldownStore>().EntryCount.ShouldBe(1);
        chat.Messages.ShouldBe(["automation ran"]);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
        (
            await db.AutomationNodeRuns.CountAsync(node => node.OutcomeCode == "action-succeeded")
        ).ShouldBe(1);
    }

    [Test]
    [Arguments(HostFeatureFlags.CustomCommands)]
    [Arguments(HostFeatureFlags.Automations)]
    public async Task AutomationAcceptance_ParentToggleWinsBeforeTransaction_MutatesNothing(
        HostFeatureFlags parent
    )
    {
        var interleaving = new AutomationDispatchTransactionInterleaving();
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync(interleaving);
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero));
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var command = await SeedAutomationCommandAsync(
            dbFactory,
            hostId,
            "limited",
            allowEveryone: true,
            cooldownSeconds: 30,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        var chat = new RecordingPublicChatSender();
        await using var services = BuildServices(
            dbFactory,
            clock: clock,
            publicChat: chat,
            realAutomations: true
        );
        await SeedAutomationFlowAsync(services, hostId, command.CommandId);
        await services
            .GetRequiredService<AutomationRuntimeService>()
            .InitializeAsync(CancellationToken.None);
        interleaving.Arm();
        var execution = services.GetRequiredService<CustomCommandExecutionService>();

        var invocation = execution
            .ExecuteAsync(
                Context(
                    "viewer",
                    "!limited",
                    tags: new Dictionary<string, string> { ["user-id"] = "viewer-id" }
                ),
                [],
                CancellationToken.None
            )
            .AsTask();
        await interleaving.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await services
                .GetRequiredService<HostFeatureService>()
                .DisableAsync(hostId, parent, CancellationToken.None);
        }
        finally
        {
            interleaving.Release();
        }

        var outcome = await invocation.WaitAsync(TimeSpan.FromSeconds(5));
        outcome
            .ShouldBeOfType<CustomCommandExecutionOutcome.Automation>()
            .Dispatch.Status.ShouldBe(AutomationDispatchStatus.FeatureDisabled);
        services.GetRequiredService<CustomCommandCooldownStore>().EntryCount.ShouldBe(0);
        chat.Messages.ShouldBeEmpty();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task AutomationParentSwitches_DisablingAndReenabling_PreservesWithoutReplay()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero));
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var command = await SeedAutomationCommandAsync(
            dbFactory,
            hostId,
            "paused",
            allowEveryone: true,
            cooldownSeconds: 30
        );
        var runtime = new RecordingAutomationRuntime();
        await using var services = BuildServices(dbFactory, clock: clock, automations: runtime);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        var message = Message("viewer", "streamer", "!paused");

        await SetFeaturesAsync(dbFactory, hostId, HostFeatureFlags.CustomCommands);
        await dispatcher.DispatchResponsesAsync(
            message,
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );
        runtime.Triggers.ShouldBeEmpty();

        await SetFeaturesAsync(
            dbFactory,
            hostId,
            HostFeatureFlags.CustomCommands | HostFeatureFlags.Automations
        );
        runtime.Triggers.ShouldBeEmpty();
        await dispatcher.DispatchResponsesAsync(
            message,
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );
        runtime.Triggers.Count.ShouldBe(1);

        await SetFeaturesAsync(dbFactory, hostId, HostFeatureFlags.Automations);
        clock.Advance(TimeSpan.FromSeconds(30));
        await dispatcher.DispatchResponsesAsync(
            message,
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );
        runtime.Triggers.Count.ShouldBe(1);

        await SetFeaturesAsync(
            dbFactory,
            hostId,
            HostFeatureFlags.CustomCommands | HostFeatureFlags.Automations
        );
        runtime.Triggers.Count.ShouldBe(1);
        await dispatcher.DispatchResponsesAsync(
            message,
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None
        );
        runtime.Triggers.Count.ShouldBe(2);

        await using var db = await dbFactory.CreateDbContextAsync();
        (
            await db
                .CustomCommandActions.OfType<AutomationCustomCommandAction>()
                .CountAsync(action => action.CustomCommandId == command.CommandId)
        ).ShouldBe(1);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
    }

    private static ServiceProvider BuildServices(
        SqliteBlokeBotDbFactory dbFactory,
        int minimumCooldownSeconds = 0,
        TimeProvider? clock = null,
        IHostStreamLivenessProvider? streams = null,
        IOverlayCueAdmissionService? overlayCues = null,
        ICustomCommandAutomationRuntime? automations = null,
        IPublicChatMessageSender? publicChat = null,
        bool realAutomations = false,
        IMessageLibraryRandomSource? random = null,
        IMessageLibraryChatterSource? chatters = null
    )
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(dbFactory);
        _ = services.AddSingleton(
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
            _ = services.AddSingleton(clock);
        }

        if (streams is not null)
        {
            _ = services.AddSingleton(streams);
        }
        if (overlayCues is not null)
        {
            _ = services.AddSingleton(overlayCues);
        }

        if (!realAutomations)
        {
            _ = services.AddSingleton<ICustomCommandAutomationRuntime>(
                automations ?? new UnavailableCustomCommandAutomationRuntime()
            );
        }
        if (random is not null)
        {
            _ = services.AddSingleton(random);
        }
        _ = services.AddSingleton<IMessageLibraryChatterSource>(
            chatters ?? new UnavailableMessageLibraryChatterSource()
        );
        _ = services.AddBlokeBotCustomCommands(CustomAnnouncementDeliveryMode.Disabled);
        if (realAutomations)
        {
            _ = services.AddSingleton(
                new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
            );
            _ = services.AddSingleton<HostFeatureService>();
            _ = services.AddSingleton(
                publicChat
                    ?? throw new ArgumentNullException(
                        nameof(publicChat),
                        "Real automations require a public-chat sender."
                    )
            );
            _ = services.AddBlokeBotAutomations();
        }
        _ = services.AddChatCommands().AddCommandModule<CustomCommandModule>();
        return services.BuildServiceProvider();
    }

    private static async Task SeedAutomationFlowAsync(
        IServiceProvider services,
        int hostId,
        int commandId
    )
    {
        var source = AutomationNode("custom-command", $$"""{"custom-command-id":{{commandId}}}""");
        var action = AutomationNode("send-chat", """{"message":"automation ran"}""");
        var outcome = await services
            .GetRequiredService<AutomationFlowService>()
            .SaveAsync(
                new(
                    null,
                    new(hostId),
                    "Command flow",
                    AutomationFlowSchema.CurrentVersion,
                    true,
                    [source, action],
                    [
                        new(
                            Guid.NewGuid(),
                            AutomationEdgeKind.Flow,
                            source.Id,
                            new("flow"),
                            action.Id,
                            new("flow")
                        ),
                    ]
                ),
                CancellationToken.None
            );
        _ = outcome.ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
    }

    private static AutomationFlowDraftNode AutomationNode(string type, string configurationJson)
    {
        using var document = JsonDocument.Parse(configurationJson);
        return new(
            new(Guid.NewGuid()),
            new(type, 1, document.RootElement.Clone()),
            AutomationExpressionLanguage.CurrentVersion,
            AutomationNodeFailurePolicy.Stop,
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty
        );
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
        _ = db.OverlayInstances.Add(
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
        _ = db.OverlayCues.Add(
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
        _ = db.CustomCommandActions.Remove(stored.Action);
        _ = await db.SaveChangesAsync();
        stored.Action = new OverlayCueCustomCommandAction
        {
            HostId = hostId,
            TargetOverlayPublicId = targetId,
            CuePublicId = cueId,
            QueuePolicy = OverlayCueQueuePolicy.Enqueue,
            ReplyOrder = replyOrder,
            ZeroArgumentMessageLibraryEntryId = command.MessageLibraryEntryId,
        };
        _ = await db.SaveChangesAsync();
        return new(command.CommandId, targetId, cueId);
    }

    private static async Task<CommandSeed> SeedAutomationCommandAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string alias,
        bool allowEveryone,
        bool allowModerators = false,
        IReadOnlyList<CustomCommandAllowedUserEditor>? allowedUsers = null,
        int cooldownSeconds = 0,
        CustomCommandInvocationLimit invocationLimit = CustomCommandInvocationLimit.Unlimited
    )
    {
        var command = await SeedCommandAsync(
            dbFactory,
            hostId,
            alias,
            ["unused"],
            allowEveryone: allowEveryone,
            allowModerators: allowModerators,
            allowedUsers: allowedUsers,
            cooldownSeconds: cooldownSeconds,
            invocationLimit: invocationLimit
        );
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db
            .CustomCommands.Include(value => value.Action)
            .SingleAsync(value => value.Id == command.CommandId);
        _ = db.CustomCommandActions.Remove(stored.Action);
        _ = await db.SaveChangesAsync();
        stored.Action = new AutomationCustomCommandAction { HostId = hostId };
        _ = await db.SaveChangesAsync();
        return command;
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
        _ = await db.SaveChangesAsync();
    }

    private static CommandResponder RecordEvents(ICollection<string> events) =>
        (response, _) =>
        {
            events.Add($"reply:{response.Message}");
            return ValueTask.CompletedTask;
        };

    private sealed class FixedRandomSource(int index) : IMessageLibraryRandomSource
    {
        public int Next(int exclusiveMaximum) => index;

        public int NextInclusive(int minimum, int maximum) => minimum;
    }

    private sealed class CountingChatterSource : IMessageLibraryChatterSource
    {
        public int CallCount { get; private set; }

        public Task<ImmutableArray<HelixChatter>> GetAsync(
            MessageLibraryRenderHost host,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(ImmutableArray<HelixChatter>.Empty);
        }
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
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = enabledFeatures,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<CommandSeed> SeedCommandAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string alias,
        string[] variants,
        CustomMessageSelectionMode selectionMode = CustomMessageSelectionMode.Sequential,
        bool allowEveryone = true,
        bool allowModerators = false,
        IReadOnlyList<CustomCommandAllowedUserEditor>? allowedUsers = null,
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
                    static (text, index) =>
                        new CustomMessageVariant { SortOrder = index, Text = text }
                )
                .ToList(),
        };
        _ = db.CustomMessageLibraryEntries.Add(entry);

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
            _ = db.CustomCounters.Add(counter);
        }

        _ = await db.SaveChangesAsync();
        var command = new CustomCommand
        {
            HostId = hostId,
            Name = $"{alias}-command",
            Enabled = true,
            AllowEveryone = allowEveryone,
            AllowModerators = allowModerators,
            AllowedUsers =
                allowedUsers
                    ?.Select(user => new CustomCommandAllowedUser
                    {
                        HostId = hostId,
                        TwitchUserId = user.TwitchUserId,
                        Login = user.Login,
                        DisplayName = user.DisplayName,
                    })
                    .ToList()
                ?? [],
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
        _ = db.CustomCommands.Add(command);
        _ = await db.SaveChangesAsync();
        _ = db.CustomCommandAliases.Add(
            new CustomCommandAlias
            {
                HostId = hostId,
                CustomCommandId = command.Id,
                Alias = CommandAliasNormalizer.Normalize(alias),
            }
        );
        _ = await db.SaveChangesAsync();
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
            Responder = responder ?? (static (_, _) => ValueTask.CompletedTask),
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
        _ = await db.SaveChangesAsync();
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

    private sealed class AutomationDispatchTransactionInterleaving : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _armed;
        private int _intercepted;

        internal Task Entered => _entered.Task;

        internal void Arm() => Volatile.Write(ref _armed, 1);

        internal void Release() => _release.SetResult();

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Volatile.Read(ref _armed) == 0
                || Interlocked.CompareExchange(ref _intercepted, 1, 0) != 0
            )
            {
                return result;
            }

            _entered.SetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class RecordingPublicChatSender : IPublicChatMessageSender
    {
        internal List<string> Messages { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(message);
            return ValueTask.FromResult<PublicChatSendOutcome>(
                new PublicChatSendOutcome.Accepted()
            );
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
            HostStreamLivenessOutcome outcome = Unavailable switch
            {
                true => new HostStreamLivenessOutcome.Unavailable(
                    HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                    new HttpRequestException("unavailable")
                ),
                false => StreamId switch
                {
                    { } current => new HostStreamLivenessOutcome.Live(
                        current,
                        DateTimeOffset.UnixEpoch
                    ),
                    _ => new HostStreamLivenessOutcome.Offline(),
                },
            };
            return IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(Result<HostStreamLivenessOutcome, Never>.Success(outcome))
            );
        }
    }

    private sealed class RecordingAutomationRuntime : ICustomCommandAutomationRuntime
    {
        public List<AutomationTrigger> Triggers { get; } = [];

        public Task<CustomCommandAutomationDispatchOutcome> DispatchAsync(
            CustomCommandAutomationDispatchRequest request,
            CancellationToken cancellationToken
        )
        {
            Triggers.Add(request.Trigger);
            return Task.FromResult<CustomCommandAutomationDispatchOutcome>(
                new CustomCommandAutomationDispatchOutcome.Dispatched(
                    new(AutomationDispatchStatus.Accepted, [new(Guid.NewGuid())])
                )
            );
        }

        public Task<IReadOnlySet<int>> AvailableCommandIdsAsync(
            AutomationHostId hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
    }
}
