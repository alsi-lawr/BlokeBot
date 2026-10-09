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
    public async Task Sqlite_SameNameScalarAndDictionaryPersistAndExecuteIndependently()
    {
        await using var database = await PointProviderFixture.SqliteFileAsync();
        await SameNameNamespacesAsync(database);
    }

    [Test, Explicit]
    public async Task PostgreSql_SameNameScalarAndDictionaryPersistAndExecuteIndependently()
    {
        await using var database = await PointProviderFixture.PostgreSqlAsync();
        await SameNameNamespacesAsync(database);
    }

    private static async Task SameNameNamespacesAsync(IDbContextFactory<BlokeBotDbContext> database)
    {
        var hostId = await SeedHostAsync(database, "streamer");
        var values = new CustomStoredValueService(database);
        var scalar = await DeclareAsync(
            values,
            hostId,
            "profile",
            CustomValueScope.User,
            CustomValueKind.Number,
            "2"
        );
        var dictionary = await DeclareAsync(
            values,
            hostId,
            "profile",
            CustomValueScope.User,
            CustomValueKind.Dictionary,
            "missing"
        );
        (
            await values.SaveDefinitionAsync(
                hostId,
                new(0, "profile", CustomValueScope.User, CustomValueKind.Text, "text", Guid.Empty),
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Invalid);
        (
            await values.SaveDefinitionAsync(
                hostId,
                new(0, "profile", CustomValueScope.User, CustomValueKind.Number, "99", Guid.Empty),
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Invalid);
        (
            await values.SaveDefinitionAsync(
                hostId,
                new(
                    0,
                    "profile",
                    CustomValueScope.User,
                    CustomValueKind.Dictionary,
                    "other",
                    Guid.Empty
                ),
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Invalid);
        _ = await SeedCommandAsync(
            database,
            hostId,
            "profile",
            [
                "{var_get|user|profile}|{dict_get|user|profile|game}|{var_set|user|profile|7}|{dict_set|user|profile|game|Celeste}|{var_inc|user|profile|2}|{dict_inc|user|profile|wins|3}|{var_get|user|profile}|{dict_get|user|profile|game}|{dict_get|user|profile|wins}",
            ]
        );
        List<string> replies = [];
        var message = Identified(
            "viewer",
            "streamer",
            "!profile",
            "same-name-effect",
            "stable-viewer"
        );
        await using (var services = BuildServices(database))
        {
            await services
                .GetRequiredService<ChatCommandDispatcher>()
                .DispatchResponsesAsync(message, RecordMessages(replies), default);
        }
        await using (var restart = BuildServices(database))
        {
            await restart
                .GetRequiredService<ChatCommandDispatcher>()
                .DispatchResponsesAsync(message, RecordMessages(replies), default);
        }
        replies.ShouldBe([
            "2|missing|7|Celeste|9|3|9|Celeste|3",
            "2|missing|7|Celeste|9|3|9|Celeste|3",
        ]);
        var reopened = new CustomStoredValueService(database);
        (
            await reopened.ValueAsync(
                hostId,
                new(scalar.Id, "stable-viewer", string.Empty),
                default
            )
        )!.Value.ShouldBe("9");
        (
            await reopened.ValueAsync(hostId, new(dictionary.Id, "stable-viewer", "game"), default)
        )!.Value.ShouldBe("Celeste");
        (
            await reopened.ValueAsync(hostId, new(dictionary.Id, "stable-viewer", "wins"), default)
        )!.Value.ShouldBe("3");
        (
            await reopened.ValueAsync(hostId, new(scalar.Id, "other-viewer", string.Empty), default)
        )!.Value.ShouldBe("2");
        (
            await reopened.ValuesAsync(hostId, dictionary.Id, "other-viewer", default)
        ).ShouldBeEmpty();
    }
}
