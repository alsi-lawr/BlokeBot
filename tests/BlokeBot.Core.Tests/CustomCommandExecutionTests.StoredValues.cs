using System.Collections.Concurrent;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CustomCommandExecutionTests
{
    [Test]
    public async Task Sqlite_StoredEffects_ConcurrentIncrementsAndRestartReplayRemainExact()
    {
        await using var database = await PointProviderFixture.SqliteFileAsync();
        await ConcurrentAndReplayAsync(database);
    }

    [Test, Explicit]
    public async Task PostgreSql_StoredEffects_ConcurrentIncrementsAndRestartReplayRemainExact()
    {
        await using var database = await PointProviderFixture.PostgreSqlAsync();
        await ConcurrentAndReplayAsync(database);
    }

    private static async Task ConcurrentAndReplayAsync(
        IDbContextFactory<BlokeBotDbContext> database
    )
    {
        var hostId = await SeedHostAsync(database, "streamer");
        var values = new CustomStoredValueService(database);
        var hugs = await DeclareAsync(
            values,
            hostId,
            "hugs",
            CustomValueScope.User,
            CustomValueKind.Number,
            "4"
        );
        _ = await DeclareAsync(
            values,
            hostId,
            "total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "0"
        );
        var failing = await SeedCommandAsync(
            database,
            hostId,
            "failure",
            ["{var_inc|user|hugs|1}|{random_between|1|100}", "Must not select again"],
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        var failedMessage = Identified(
            "viewer",
            "streamer",
            "!failure",
            "failed-delivery",
            "stable-viewer"
        );
        await using (var services = BuildServices(database, random: new ReplayRandom(17)))
        {
            _ = await Should.ThrowAsync<IOException>(async () =>
                await services
                    .GetRequiredService<ChatCommandDispatcher>()
                    .DispatchResponsesAsync(
                        failedMessage,
                        static (_, _) =>
                            ValueTask.FromException(
                                new IOException("injected chat delivery failure")
                            ),
                        default
                    )
            );
        }
        (
            await values.ValueAsync(hostId, new(hugs.Id, "stable-viewer", string.Empty), default)
        )!.Value.ShouldBe("5");
        await using (var verify = await database.CreateDbContextAsync())
        {
            (await verify.CustomCommandComputedResults.SingleAsync()).Reply.ShouldBe("5|17");
            (
                await verify.CustomMessageLibraryEntries.SingleAsync(x =>
                    x.Id == failing.MessageLibraryEntryId
                )
            ).CurrentVariantIndex.ShouldBe(1);
        }
        List<string> replayReplies = [];
        await using (var restarted = BuildServices(database, random: new ReplayRandom(92)))
        {
            var dispatcher = restarted.GetRequiredService<ChatCommandDispatcher>();
            await dispatcher.DispatchResponsesAsync(
                Identified(
                    "renamed-viewer",
                    "streamer",
                    "!failure",
                    "failed-delivery",
                    "stable-viewer"
                ),
                RecordMessages(replayReplies),
                default
            );
            await dispatcher.DispatchResponsesAsync(
                Identified(
                    "someone-else",
                    "streamer",
                    "!failure",
                    "failed-delivery",
                    "other-viewer"
                ),
                RecordMessages(replayReplies),
                default
            );
            await dispatcher.DispatchResponsesAsync(
                Identified("viewer", "streamer", "!failure", "another-message", "stable-viewer"),
                RecordMessages(replayReplies),
                default
            );
        }
        replayReplies.ShouldBe(["5|17"]);
        (
            await values.ValueAsync(hostId, new(hugs.Id, "stable-viewer", string.Empty), default)
        )!.Value.ShouldBe("5");
        _ = await SeedCommandAsync(
            database,
            hostId,
            "hug",
            ["{var_inc|user|hugs|1}|{var_inc|global|total|1}|{var_get|user|hugs}"]
        );
        await using var concurrent = BuildServices(database);
        var commands = concurrent.GetRequiredService<ChatCommandDispatcher>();
        ConcurrentBag<string> replies = [];
        await Task.WhenAll(
            Enumerable
                .Range(0, 24)
                .Select(index =>
                    commands
                        .DispatchResponsesAsync(
                            Identified(
                                "viewer",
                                "streamer",
                                "!hug",
                                $"parallel-{index}",
                                "stable-viewer"
                            ),
                            (response, _) =>
                            {
                                replies.Add(response.Message);
                                return ValueTask.CompletedTask;
                            },
                            default
                        )
                        .AsTask()
                )
        );
        replies.Count.ShouldBe(24);
        replies
            .Select(x =>
                long.Parse(x.Split('|')[0], System.Globalization.CultureInfo.InvariantCulture)
            )
            .Order()
            .ShouldBe(Enumerable.Range(6, 24).Select(x => (long)x));
        replies.ShouldAllBe(x => x.Split('|')[0] == x.Split('|')[2]);
        (
            await values.ValueAsync(hostId, new(hugs.Id, "stable-viewer", string.Empty), default)
        )!.Value.ShouldBe("29");
        await using var final = await database.CreateDbContextAsync();
        (
            await final.CustomStoredValues.SingleAsync(x => x.ViewerId == string.Empty)
        ).Number.ShouldBe(24);
        _ = await SeedCommandAsync(
            database,
            hostId,
            "plain-first",
            ["plain", "{var_inc|user|hugs|1}"]
        );
        List<string> plainReplies = [];
        var plainMessage = Identified(
            "viewer",
            "streamer",
            "!plain-first",
            "plain-selection",
            "plain-viewer"
        );
        await commands.DispatchResponsesAsync(plainMessage, RecordMessages(plainReplies), default);
        await commands.DispatchResponsesAsync(plainMessage, RecordMessages(plainReplies), default);
        plainReplies.ShouldBe(["plain", "plain"]);
        (
            await values.ValueAsync(hostId, new(hugs.Id, "plain-viewer", string.Empty), default)
        )!.Value.ShouldBe("4");
    }

    [Test]
    public async Task Sqlite_NestedLiteralStores_IsolationAndFailedReplyRollbackArePreserved()
    {
        await using var database = await PointProviderFixture.SqliteFileAsync();
        await NestedAndIsolationAsync(database);
    }

    [Test, Explicit]
    public async Task PostgreSql_NestedLiteralStores_IsolationAndFailedReplyRollbackArePreserved()
    {
        await using var database = await PointProviderFixture.PostgreSqlAsync();
        await NestedAndIsolationAsync(database);
    }

    private static async Task NestedAndIsolationAsync(IDbContextFactory<BlokeBotDbContext> database)
    {
        var hostId = await SeedHostAsync(database, "streamer");
        var otherHost = await SeedHostAsync(database, "other");
        var values = new CustomStoredValueService(database);
        var longName = new string('n', 8192);
        var longDefault = string.Concat(
            Enumerable.Repeat("literal {var_inc|global|total|1}|😀 ", 40)
        );
        _ = await DeclareAsync(
            values,
            hostId,
            longName,
            CustomValueScope.User,
            CustomValueKind.Text,
            longDefault
        );
        var bio = await DeclareAsync(
            values,
            hostId,
            "bio",
            CustomValueScope.User,
            CustomValueKind.Text,
            longDefault
        );
        var peer = await DeclareAsync(
            values,
            otherHost,
            "bio",
            CustomValueScope.User,
            CustomValueKind.Text,
            "other-channel"
        );
        var profile = await DeclareAsync(
            values,
            hostId,
            "profile",
            CustomValueScope.User,
            CustomValueKind.Dictionary,
            "missing"
        );
        var total = await DeclareAsync(
            values,
            hostId,
            "total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "0"
        );
        _ = await SeedCommandAsync(database, hostId, "named", ["{var_get|user|{arg1}}"]);
        var bioCommand = await SeedCommandAsync(
            database,
            hostId,
            "bio",
            ["{var_set|user|bio|{args}}"]
        );
        await using (var configure = await database.CreateDbContextAsync())
        {
            var command = await configure
                .CustomCommands.Include(x => x.Action)
                .SingleAsync(x => x.Id == bioCommand.CommandId);
            command.SingleArgument = true;
            command.Action.ZeroArgumentMessageLibraryEntryId = null;
            _ = await configure.SaveChangesAsync();
        }
        _ = await SeedCommandAsync(
            database,
            hostId,
            "profile",
            ["{dict_set|user|profile|{arg1}|{arg2}}/{dict_get|user|profile|{arg1}}"]
        );
        _ = await SeedCommandAsync(
            database,
            hostId,
            "number",
            ["{dict_inc|user|profile|{arg1}|{random_between|{arg2}|{arg2}}}"]
        );
        var broken = await SeedCommandAsync(
            database,
            hostId,
            "broken",
            ["{var_inc|global|total|1}{dict_inc|user|profile|{arg1}|{arg2}}", "unused"],
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        await using var services = BuildServices(database, random: new ReplayRandom(0));
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];
        await dispatcher.DispatchResponsesAsync(
            Identified("first-name", "streamer", $"!named {longName}", "default", "stable-id"),
            RecordMessages(replies),
            default
        );
        replies.Single().ShouldBe(longDefault);
        var literal = "I like Earl Grey tea {var_inc|global|total|100}|still literal  ";
        await dispatcher.DispatchResponsesAsync(
            Identified("renamed", "streamer", $"!bio {literal}", "bio", "stable-id"),
            RecordMessages(replies),
            default
        );
        replies.Last().ShouldBe(literal);
        (
            await values.ValueAsync(hostId, new(bio.Id, "stable-id", string.Empty), default)
        )!.Value.ShouldBe(literal);
        (
            await values.ValueAsync(hostId, new(bio.Id, "second-id", string.Empty), default)
        )!.Value.ShouldBe(longDefault);
        (
            await values.ValueAsync(otherHost, new(peer.Id, "stable-id", string.Empty), default)
        )!.Value.ShouldBe("other-channel");
        var key = new string('k', 8192);
        await dispatcher.DispatchResponsesAsync(
            Identified(
                "viewer",
                "streamer",
                $"!profile {key} {{var_inc|global|total|50}}|data",
                "dictionary",
                "stable-id"
            ),
            RecordMessages(replies),
            default
        );
        (
            await values.ValueAsync(hostId, new(profile.Id, "stable-id", key), default)
        )!.Value.ShouldBe("{var_inc|global|total|50}|data");
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!number wins 7", "number", "stable-id"),
            RecordMessages(replies),
            default
        );
        (
            await values.ValueAsync(hostId, new(profile.Id, "stable-id", "wins"), default)
        )!.Value.ShouldBe("7");
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", $"!broken {key} 3", "broken", "stable-id"),
            RecordMessages(replies),
            default
        );
        replies.Last().ShouldNotContain("still literal");
        (
            await values.ValueAsync(hostId, new(total.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe("0");
        await using (var verify = await database.CreateDbContextAsync())
        {
            (
                await verify.CustomCommandInvocationClaims.AnyAsync(x =>
                    x.CustomCommandId == broken.CommandId
                )
            ).ShouldBeFalse();
            (
                await verify.CustomMessageLibraryEntries.SingleAsync(x =>
                    x.Id == broken.MessageLibraryEntryId
                )
            ).CurrentVariantIndex.ShouldBe(0);
            (
                await verify.CustomCommandComputedResults.AnyAsync(x => x.InvocationId == "broken")
            ).ShouldBeFalse();
        }
        var original = (
            await values.ValueAsync(hostId, new(profile.Id, "stable-id", "wins"), default)
        )!;
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!number wins 1", "newer-value", "stable-id"),
            RecordMessages(replies),
            default
        );
        (
            await values.SaveValueAsync(hostId, original, CustomValueKind.Number, "99", default)
        ).Status.ShouldBe(CustomValueEditStatus.Conflict);
        (await values.ResetValueAsync(hostId, original, default)).Status.ShouldBe(
            CustomValueEditStatus.Conflict
        );
        var preview = await values.AffectedValuesAsync(hostId, profile.Id, default);
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!number wins 1", "newer-again", "stable-id"),
            RecordMessages(replies),
            default
        );
        (await values.DeleteDefinitionAsync(hostId, profile, preview, default)).Status.ShouldBe(
            CustomValueEditStatus.Conflict
        );
        var latest = (
            await values.ValueAsync(hostId, new(profile.Id, "stable-id", "wins"), default)
        )!;
        (await values.ResetValueAsync(hostId, latest, default)).Status.ShouldBe(
            CustomValueEditStatus.Saved
        );
        (
            await values.ValueAsync(hostId, new(profile.Id, "stable-id", key), default)
        )!.Saved.ShouldBeTrue();
        (
            await values.ValueAsync(hostId, new(profile.Id, "stable-id", "wins"), default)
        )!.Saved.ShouldBeFalse();
        var sandbox = await values.SandboxAsync(hostId, "stable-id", default);
        var context = new ChatCommandContext
        {
            Message = Identified("viewer", "streamer", "!preview", "sandbox", "stable-id"),
            CommandName = "preview",
            Responder = RecordMessages(replies),
        };
        for (var index = 0; index < 3; index++)
        {
            CustomCommandTemplateRenderer
                .RenderCommandPreview(
                    "{random_from|{var_inc|global|total|1}|{var_inc|global|total|100}}:{var_get|global|total}",
                    context,
                    [],
                    null,
                    sandbox
                )
                .ShouldBe("1:1");
        }
        (
            await values.ValueAsync(hostId, new(total.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe("0");
        _ = await SeedCommandAsync(
            database,
            hostId,
            "selected",
            [
                "{random_from|{var_inc|global|total|{var_inc|global|total|1}}|{var_inc|global|total|100}}:{var_get|global|total}",
            ]
        );
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!selected", "selected-once", "stable-id"),
            RecordMessages(replies),
            default
        );
        replies.Last().ShouldBe("2:2");
        (
            await values.ValueAsync(hostId, new(total.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe("2");
    }

    internal static async Task<CustomValueDefinitionDraft> DeclareAsync(
        CustomStoredValueService values,
        int hostId,
        string name,
        CustomValueScope scope,
        CustomValueKind kind,
        string initial
    )
    {
        (
            await values.SaveDefinitionAsync(
                hostId,
                new(0, name, scope, kind, initial, Guid.Empty),
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        return (await values.DefinitionsAsync(hostId, default)).Single(x =>
            x.Name == name && x.Scope == scope && x.Kind == kind
        );
    }

    private static ChatMessage Identified(
        string login,
        string channel,
        string text,
        string id,
        string viewerId
    ) =>
        Message(
            login,
            channel,
            text,
            new Dictionary<string, string> { ["id"] = id, ["user-id"] = viewerId }
        );

    private sealed class ReplayRandom(int number) : IMessageLibraryRandomSource
    {
        public int Next(int exclusiveMaximum) => 0;

        public int NextInclusive(int minimum, int maximum) => minimum == maximum ? minimum : number;
    }
}
