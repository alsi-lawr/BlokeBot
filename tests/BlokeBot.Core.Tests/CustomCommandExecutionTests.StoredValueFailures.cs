using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CustomCommandExecutionTests
{
    [Test]
    public async Task Sqlite_StoredSaveFailureAndMissingIdentityCannotPartiallyApplyOrConsumeAdmission()
    {
        var failure = new StoredSaveFailure();
        await using var database = await PointProviderFixture.SqliteFileAsync(failure);
        await FailedAdmissionAsync(database, failure);
    }

    [Test, Explicit]
    public async Task PostgreSql_StoredSaveFailureAndMissingIdentityCannotPartiallyApplyOrConsumeAdmission()
    {
        var failure = new StoredSaveFailure();
        await using var database = await PointProviderFixture.PostgreSqlAsync(failure);
        await FailedAdmissionAsync(database, failure);
    }

    private static async Task FailedAdmissionAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        StoredSaveFailure failure
    )
    {
        var hostId = await SeedHostAsync(database, "streamer");
        var values = new CustomStoredValueService(database);
        var total = await DeclareAsync(
            values,
            hostId,
            "total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "0"
        );
        var bounded = await DeclareAsync(
            values,
            hostId,
            "bounded",
            CustomValueScope.Global,
            CustomValueKind.Number,
            long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        var command = await SeedCommandAsync(
            database,
            hostId,
            "store",
            ["{var_inc|global|total|1}", "{var_inc|global|total|100}"],
            cooldownSeconds: 30,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        _ = await SeedCommandAsync(
            database,
            hostId,
            "overflow",
            ["{var_inc|global|total|1}:{var_inc|global|bounded|1}"]
        );
        await using var services = BuildServices(database);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!store", string.Empty, "stable"),
            RecordMessages(replies),
            default
        );
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!store", "missing-viewer", string.Empty),
            RecordMessages(replies),
            default
        );
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!overflow", "overflow", "stable"),
            RecordMessages(replies),
            default
        );
        (
            await values.ValueAsync(hostId, new(total.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe("0");
        (
            await values.ValueAsync(hostId, new(bounded.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe(
            long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!store", "save-failure", "stable"),
            RecordMessages(replies),
            default
        );
        (
            await values.ValueAsync(hostId, new(total.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe("0");
        await using (var verify = await database.CreateDbContextAsync())
        {
            (await verify.CustomCommandComputedResults.ToListAsync()).ShouldBeEmpty();
            (await verify.CustomCommandInvocationClaims.ToListAsync()).ShouldBeEmpty();
            (
                await verify.CustomMessageLibraryEntries.SingleAsync(x =>
                    x.Id == command.MessageLibraryEntryId
                )
            ).CurrentVariantIndex.ShouldBe(0);
        }
        failure.Enabled = false;
        await dispatcher.DispatchResponsesAsync(
            Identified("viewer", "streamer", "!store", "save-failure", "stable"),
            RecordMessages(replies),
            default
        );
        replies.Last().ShouldBe("1");
        (
            await values.ValueAsync(hostId, new(total.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe("1");
    }

    [Test]
    public async Task SingleArgumentModeConsumesOnlyDeclaredPhraseAndKeepsEmptyAndLegacyRoutes()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "streamer");
        var single = await SeedCommandAsync(database, hostId, "single", ["{args}/{arg1}/{arg2}"]);
        var legacy = await SeedCommandAsync(database, hostId, "legacy", ["{args}/{arg1}/{arg2}"]);
        await using (var seed = database.CreateDbContext())
        {
            var command = await seed
                .CustomCommands.Include(x => x.Action)
                .SingleAsync(x => x.Id == single.CommandId);
            command.SingleArgument = true;
            var empty = new CustomMessageLibraryEntry
            {
                HostId = hostId,
                Name = "Empty route",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Variants = [new() { Text = "zero" }],
            };
            _ = seed.CustomMessageLibraryEntries.Add(empty);
            _ = await seed.SaveChangesAsync();
            command.Action.ZeroArgumentMessageLibraryEntryId = empty.Id;
            _ = await seed.SaveChangesAsync();
        }
        await using var services = BuildServices(database);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];
        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!single Earl  Grey tea  "),
            RecordMessages(replies),
            default
        );
        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!single   "),
            RecordMessages(replies),
            default
        );
        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!legacy Earl Grey"),
            RecordMessages(replies),
            default
        );
        await dispatcher.DispatchResponsesAsync(
            Message("viewer", "streamer", "!legacy Earl Grey tea"),
            RecordMessages(replies),
            default
        );
        replies.ShouldBe(["Earl  Grey tea  /Earl  Grey tea  /", "zero", "Earl Grey/Earl/Grey"]);
        _ = legacy;
    }

    private sealed class StoredSaveFailure : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; } = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            Enabled && eventData.Context?.ChangeTracker.Entries<CustomStoredValue>().Any() == true
                ? ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("Injected finite storage failure")
                )
                : ValueTask.FromResult(result);
    }
}
