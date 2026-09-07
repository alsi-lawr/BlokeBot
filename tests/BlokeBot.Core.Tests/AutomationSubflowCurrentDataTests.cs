using System.Data.Common;
using System.IO.Compression;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task SubflowLatest_ExistingCurrentDataConvertsAtomicallyAndBaselineFrozenRunResumesExactlyOnce()
    {
        var interruption = new CurrentConversionInterruption();
        await using var fixture = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [interruption]
        );
        await RestorePreLatestDatabaseAsync(fixture);
        await using (var migration = await fixture.Database.CreateDbContextAsync())
        {
            await migration.Database.MigrateAsync();
        }
        Dictionary<Guid, string> snapshots;
        string history;
        string current;
        AutomationRunId runId;
        await using (var before = await fixture.Database.CreateDbContextAsync())
        {
            snapshots = await before
                .AutomationSubflowRevisions.AsNoTracking()
                .ToDictionaryAsync(row => row.Id, row => row.SnapshotJson);
            history = await FrozenFactsAsync(before);
            current = await CurrentFactsAsync(before);
            runId = new((await before.AutomationFlowRuns.SingleAsync()).Id);
        }
        var conversion = new AutomationSubflowCurrentDataConversion(
            fixture.Database,
            fixture.Clock
        );
        interruption.Armed = true;
        _ = await Should.ThrowAsync<DbUpdateException>(() =>
            conversion.ApplyAsync(CancellationToken.None)
        );
        interruption.Armed = false;
        await using (var unchanged = await fixture.Database.CreateDbContextAsync())
        {
            (await CurrentFactsAsync(unchanged)).ShouldBe(current);
            (await FrozenFactsAsync(unchanged)).ShouldBe(history);
            (await unchanged.AutomationSubflowRevisions.CountAsync()).ShouldBe(snapshots.Count);
        }
        await conversion.ApplyAsync(CancellationToken.None);
        string converted;
        await using (var after = await fixture.Database.CreateDbContextAsync())
        {
            (await FrozenFactsAsync(after)).ShouldBe(history);
            foreach (var (id, json) in snapshots)
            {
                (
                    await after.AutomationSubflowRevisions.SingleAsync(row => row.Id == id)
                ).SnapshotJson.ShouldBe(json);
            }
            (await after.AutomationSubflowRevisions.CountAsync()).ShouldBe(snapshots.Count + 1);
            (await after.AutomationSubflowCallers.CountAsync()).ShouldBe(1);
            (await after.AutomationSubflowNestedCallers.CountAsync()).ShouldBe(1);
            (
                await after
                    .AutomationFlowNodes.Where(node =>
                        node.DefinitionId == AutomationSubflowDefinitions.Invoke
                    )
                    .Select(node => node.DefinitionSchemaVersion)
                    .SingleAsync()
            ).ShouldBe(2);
            converted = await CurrentFactsAsync(after);
        }
        await conversion.ApplyAsync(CancellationToken.None);
        await using (var unchanged = await fixture.Database.CreateDbContextAsync())
        {
            (await CurrentFactsAsync(unchanged)).ShouldBe(converted);
            (await FrozenFactsAsync(unchanged)).ShouldBe(history);
            (await unchanged.AutomationSubflowRevisions.CountAsync()).ShouldBe(snapshots.Count + 1);
        }
        var library = Subflows(fixture);
        var leaf = snapshots
            .Values.Select(AutomationSubflowSerialization.Restore)
            .Single(revision => revision.Graph.Name == "Pre-amendment leaf");
        _ = await Publish(library, Subflow(fixture.HostId) with { Id = leaf.SubflowId });
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe(["frozen-after-upgrade"]);
        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe(["frozen-after-upgrade"]);
        await using var completed = await fixture.Database.CreateDbContextAsync();
        (await completed.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        (await completed.AutomationFlowRuns.SingleAsync()).DefinitionJson.ShouldBe(
            JsonDocument
                .Parse(history)
                .RootElement.GetProperty("Runs")[0]
                .GetProperty("DefinitionJson")
                .GetString()
        );
    }

    private static async Task RestorePreLatestDatabaseAsync(RuntimeFixture fixture)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-pre-latest-{Guid.NewGuid():N}.sqlite"
        );
        try
        {
            await using (
                var input = File.OpenRead(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Fixtures",
                        "Automations",
                        "pre-latest-frozen.sqlite.gz"
                    )
                )
            )
            await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            await using (var output = File.Create(path))
            {
                await gzip.CopyToAsync(output);
            }
            await using var source = new SqliteConnection(
                $"Data Source={path};Mode=ReadOnly;Pooling=False"
            );
            await source.OpenAsync();
            await using var db = await fixture.Database.CreateDbContextAsync();
            await db.Database.OpenConnectionAsync();
            source.BackupDatabase((SqliteConnection)db.Database.GetDbConnection());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> CurrentFactsAsync(BlokeBotDbContext db) =>
        JsonSerializer.Serialize(
            new
            {
                Current = await db
                    .AutomationSubflows.AsNoTracking()
                    .OrderBy(row => row.Id)
                    .ToArrayAsync(),
                Nodes = await db
                    .AutomationFlowNodes.AsNoTracking()
                    .OrderBy(row => row.Id)
                    .ToArrayAsync(),
                Edges = await db
                    .AutomationFlowEdges.AsNoTracking()
                    .OrderBy(row => row.Id)
                    .ToArrayAsync(),
            }
        );

    private static async Task<string> FrozenFactsAsync(BlokeBotDbContext db) =>
        JsonSerializer.Serialize(
            new
            {
                Runs = await db
                    .AutomationFlowRuns.AsNoTracking()
                    .OrderBy(row => row.Id)
                    .ToArrayAsync(),
                Nodes = await db
                    .AutomationNodeRuns.AsNoTracking()
                    .OrderBy(row => row.Id)
                    .ToArrayAsync(),
                References = await db
                    .AutomationSubflowRunReferences.AsNoTracking()
                    .OrderBy(row => row.RevisionId)
                    .ToArrayAsync(),
                Traces = await db
                    .AutomationTraces.AsNoTracking()
                    .OrderBy(row => row.Id)
                    .ToArrayAsync(),
                Events = await db
                    .AutomationTraceEvents.AsNoTracking()
                    .OrderBy(row => row.TraceId)
                    .ThenBy(row => row.Sequence)
                    .ToArrayAsync(),
            }
        );

    private sealed class CurrentConversionInterruption : DbCommandInterceptor
    {
        internal bool Armed { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        ) =>
            Armed
            && command.CommandText.StartsWith(
                "INSERT INTO \"automation_subflow_revisions\"",
                StringComparison.Ordinal
            )
                ? throw new InvalidOperationException("Interrupted current-data conversion.")
                : ValueTask.FromResult(result);
    }
}
