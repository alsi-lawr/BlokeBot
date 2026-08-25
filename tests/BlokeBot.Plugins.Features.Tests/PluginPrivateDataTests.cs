using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Plugins.Features.Tests;

public sealed class PluginPrivateDataTests
{
    [Test]
    public async Task PluginAndHostIdentity_PartitionsDurablePrivateDataWithoutExposingPaths()
    {
        await using var root = new TemporaryPrivateDataRoot();
        var store = root.Store();
        var first = Identity("community.first", hostId: 11);
        var secondHost = Identity("community.first", hostId: 22);
        var otherPlugin = Identity("community.second", hostId: 11);
        var none = new PluginValue.Map([]);

        _ = (
            await store.ExecuteAsync(
                first,
                "CREATE TABLE entries (host_id INTEGER NOT NULL, value TEXT NOT NULL);",
                none,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Changed>();
        _ = (
            await store.ExecuteAsync(
                first,
                "INSERT INTO entries (host_id, value) VALUES ($host, $value);",
                Parameters(
                    ("host", new PluginValue.Number(first.Host.Value)),
                    ("value", new PluginValue.String("first"))
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Changed>();
        _ = (
            await store.ExecuteAsync(
                secondHost,
                "INSERT INTO entries (host_id, value) VALUES ($host, $value);",
                Parameters(
                    ("host", new PluginValue.Number(secondHost.Host.Value)),
                    ("value", new PluginValue.String("second"))
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Changed>();

        var firstRows = (
            await store.QueryAsync(
                first,
                "SELECT value FROM entries WHERE host_id = $host;",
                Parameters(("host", new PluginValue.Number(first.Host.Value))),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rows>();
        Value(firstRows).ShouldBe("first");
        var secondRows = (
            await store.QueryAsync(
                secondHost,
                "SELECT value FROM entries WHERE host_id = $host;",
                Parameters(("host", new PluginValue.Number(secondHost.Host.Value))),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rows>();
        Value(secondRows).ShouldBe("second");
        (
            await store.QueryAsync(
                otherPlugin,
                "SELECT value FROM entries;",
                none,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginSqliteOutcome.Rejected>()
            .Code.ShouldBe(PluginSqliteRejectionCode.StatementFailed);

        var restarted = root.Store();
        var retained = (
            await restarted.QueryAsync(
                first,
                "SELECT value FROM entries WHERE host_id = $host;",
                Parameters(("host", new PluginValue.Number(first.Host.Value))),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rows>();
        Value(retained).ShouldBe("first");
    }

    [Test]
    public async Task PrivateConnection_RejectsDatabaseAndFileEscapeBeforeAnyStatementRuns()
    {
        await using var root = new TemporaryPrivateDataRoot();
        var store = root.Store();
        var first = Identity("community.first", hostId: 11);
        var second = Identity("community.second", hostId: 22);
        var none = new PluginValue.Map([]);
        _ = await store.ExecuteAsync(
            second,
            "CREATE TABLE entries (value TEXT NOT NULL);",
            none,
            CancellationToken.None
        );
        _ = await store.ExecuteAsync(
            second,
            "INSERT INTO entries (value) VALUES ('safe');",
            none,
            CancellationToken.None
        );

        var attach = await store.ExecuteAsync(
            first,
            "ATTACH DATABASE $path AS victim; UPDATE victim.entries SET value = 'changed';",
            Parameters(("path", new PluginValue.String(root.DatabasePath(second.Plugin.PluginId)))),
            CancellationToken.None
        );
        attach
            .ShouldBeOfType<PluginSqliteOutcome.Rejected>()
            .Code.ShouldBe(PluginSqliteRejectionCode.InvalidStatement);

        var export = Path.Combine(root.RootPath, "escaped.db");
        var vacuum = await store.ExecuteAsync(
            first,
            "VACUUM INTO $path;",
            Parameters(("path", new PluginValue.String(export))),
            CancellationToken.None
        );
        vacuum
            .ShouldBeOfType<PluginSqliteOutcome.Rejected>()
            .Code.ShouldBe(PluginSqliteRejectionCode.InvalidStatement);
        File.Exists(export).ShouldBeFalse();

        var mixed = await store.ExecuteAsync(
            first,
            "CREATE TABLE escaped (value TEXT); ATTACH DATABASE $path AS victim;",
            Parameters(("path", new PluginValue.String(root.DatabasePath(second.Plugin.PluginId)))),
            CancellationToken.None
        );
        mixed
            .ShouldBeOfType<PluginSqliteOutcome.Rejected>()
            .Code.ShouldBe(PluginSqliteRejectionCode.InvalidStatement);
        var safeMultiple = await store.ExecuteAsync(
            first,
            "CREATE TABLE first (value TEXT); CREATE TABLE second (value TEXT);",
            none,
            CancellationToken.None
        );
        safeMultiple
            .ShouldBeOfType<PluginSqliteOutcome.Rejected>()
            .Code.ShouldBe(PluginSqliteRejectionCode.InvalidStatement);

        var victim = (
            await store.QueryAsync(
                second,
                "SELECT value FROM entries;",
                none,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rows>();
        Value(victim).ShouldBe("safe");
        var escapedTables = (
            await store.QueryAsync(
                first,
                "SELECT name FROM sqlite_schema WHERE type = 'table' AND name IN ('escaped', 'first', 'second');",
                none,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rows>();
        escapedTables.Values.ShouldBeEmpty();

        _ = (
            await store.ExecuteAsync(
                first,
                "CREATE TABLE own_data (host_id INTEGER NOT NULL, value TEXT NOT NULL);",
                none,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Changed>();
        _ = (
            await store.ExecuteAsync(
                first,
                "INSERT INTO own_data (host_id, value) VALUES ($host, $value);",
                Parameters(
                    ("host", new PluginValue.Number(first.Host.Value)),
                    ("value", new PluginValue.String("normal"))
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Changed>();
        var own = (
            await store.QueryAsync(
                first,
                "SELECT value FROM own_data WHERE host_id = $host;",
                Parameters(("host", new PluginValue.Number(first.Host.Value))),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rows>();
        Value(own).ShouldBe("normal");

        var database = root.DatabasePath(first.Plugin.PluginId);
        await using (var direct = new SqliteConnection($"Data Source={database}"))
        {
            await direct.OpenAsync();
            await using var create = direct.CreateCommand();
            create.CommandText = "CREATE VIRTUAL TABLE external_search USING fts5(value);";
            _ = await create.ExecuteNonQueryAsync();
        }
        var virtualTable = await store.QueryAsync(
            first,
            "SELECT value FROM external_search;",
            none,
            CancellationToken.None
        );
        virtualTable
            .ShouldBeOfType<PluginSqliteOutcome.Rejected>()
            .Code.ShouldBe(PluginSqliteRejectionCode.InvalidStatement);
    }

    [Test]
    public async Task LifecycleMigration_FaultRollsBackThenRecoveryCommitsOnceAndPurgeDeletes()
    {
        await using var root = new TemporaryPrivateDataRoot();
        var store = root.Store();
        var runtime = new PluginRuntimeSnapshotRegistry();
        var runner = new SqlMigrationRunner(store) { FailAfterSql = true };
        var owner = new PluginPrivateDataLifecycleOwner(
            store,
            runner,
            runtime,
            TimeProvider.System
        );
        var initial = Manifest("1.0.0", migrations: []);
        var initialContext = Context(initial, runtime);

        _ = (
            await owner.MigrateAsync(initialContext, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();

        var migration = Migration("1.0.0", "1.2.0");
        var update = Manifest("1.2.0", [migration]);
        var updateContext = Context(update, runtime);
        _ = (
            await owner.MigrateAsync(updateContext, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Failed>();
        _ = (
            await store.QueryAsync(
                Identity(update.Manifest.Id.Value, 1, update.Manifest.Release),
                "SELECT value FROM migrated;",
                new PluginValue.Map([]),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rejected>();

        runner.FailAfterSql = false;
        _ = (
            await owner.MigrateAsync(updateContext, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();
        runner.Invocations.ShouldBe(2);
        _ = (
            await owner.MigrateAsync(updateContext, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();
        runner.Invocations.ShouldBe(2);
        var retained = (
            await store.QueryAsync(
                Identity(update.Manifest.Id.Value, 1, update.Manifest.Release),
                "SELECT value FROM migrated;",
                new PluginValue.Map([]),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rows>();
        Value(retained).ShouldBe("updated");

        Publish(runtime, updateContext, PluginLifecyclePhase.Purging);
        _ = (
            await owner.PurgeAsync(
                new(update.Manifest.Id, updateContext.Fence),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();
        _ = (
            await store.QueryAsync(
                Identity(update.Manifest.Id.Value, 1, update.Manifest.Release),
                "SELECT value FROM migrated;",
                new PluginValue.Map([]),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rejected>();
    }

    [Test]
    public async Task LifecycleMigration_GenerationChangeRollsBackBeforeVersionCommit()
    {
        await using var root = new TemporaryPrivateDataRoot();
        var store = root.Store();
        var runtime = new PluginRuntimeSnapshotRegistry();
        var initial = Manifest("1.0.0", migrations: []);
        var runner = new SqlMigrationRunner(store);
        var owner = new PluginPrivateDataLifecycleOwner(
            store,
            runner,
            runtime,
            TimeProvider.System
        );
        var initialContext = Context(initial, runtime);
        _ = await owner.MigrateAsync(initialContext, CancellationToken.None);

        var update = Manifest("1.2.0", [Migration("1.0.0", "1.2.0")]);
        var updateContext = Context(update, runtime);
        runner.AfterSql = () =>
        {
            var stale = new PluginMigrationContext(
                updateContext.Installation,
                new(PluginLifecycleOperationId.New(), Generation(2)),
                updateContext.Package!
            );
            Publish(runtime, stale, PluginLifecyclePhase.Migrating);
        };

        _ = (
            await owner.MigrateAsync(updateContext, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Failed>();
        _ = (
            await store.QueryAsync(
                Identity(update.Manifest.Id.Value, 1, update.Manifest.Release),
                "SELECT value FROM migrated;",
                new PluginValue.Map([]),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginSqliteOutcome.Rejected>();
    }

    private static PluginMigrationContext Context(
        ValidatedPluginManifest manifest,
        PluginRuntimeSnapshotRegistry runtime
    )
    {
        var fence = new PluginLifecycleFence(PluginLifecycleOperationId.New(), Generation(1));
        var installation = new PluginInstallationIdentity(
            manifest.Manifest.Id,
            manifest.Manifest.Release
        );
        var module = manifest.Manifest.EntryModule;
        var package = new PluginLifecyclePackage(
            installation,
            new(
                new(
                    installation,
                    PluginRuntimeIdentifier.LinuxX64,
                    module,
                    [new(module, "lua/main.lua")]
                ),
                Path.GetTempPath()
            )
            {
                Manifest = manifest,
            },
            Path.Combine(Path.GetTempPath(), "blokebot-plugin-migration-tests"),
            new UnusedDispatcher(),
            NullLogger<PluginWorkerClient>.Instance
        );
        var context = new PluginMigrationContext(installation, fence, package);
        Publish(runtime, context, PluginLifecyclePhase.Migrating);
        return context;
    }

    private static void Publish(
        PluginRuntimeSnapshotRegistry runtime,
        PluginMigrationContext context,
        PluginLifecyclePhase phase
    )
    {
        var now = DateTimeOffset.UtcNow;
        _ = runtime.Publish(
            new(
                context.Installation.PluginId,
                context.Installation,
                context.Fence.OperationId,
                context.Fence.Generation,
                null,
                phase,
                phase == PluginLifecyclePhase.Purging
                    ? PluginLifecycleOperationKind.Purge
                    : PluginLifecycleOperationKind.Activate,
                null,
                false,
                null,
                PluginLifecycleOutcome.Progress(PluginLifecycleOutcomeCode.Migrating, now),
                1,
                now
            ),
            worker: null
        );
    }

    private static ValidatedPluginManifest Manifest(
        string version,
        ImmutableArray<PluginMigrationDescriptor> migrations
    )
    {
        var accepted = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestJson.Validate(
                    PluginContractFixtures.CompleteManifestJson(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        _ = SemanticVersion.TryCreate(version, out var release);
        var modified = accepted.Manifest with
        {
            Release = accepted.Manifest.Release with { DeclaredVersion = release },
            Migrations = migrations,
        };
        return (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestValidator.Validate(modified, PluginContractFixtures.CompatibleHost())
        ).Manifest;
    }

    private static PluginMigrationDescriptor Migration(string from, string to)
    {
        _ = PluginMigrationId.TryCreate("private-data", out var id);
        _ = SemanticVersion.TryCreate(from, out var fromVersion);
        _ = SemanticVersion.TryCreate(to, out var toVersion);
        _ = PluginLuaModuleId.TryCreate("main", out var module);
        return new(id, fromVersion, toVersion, module, "migrate");
    }

    private static PluginWorkerInvocationIdentity Identity(
        string plugin,
        int hostId,
        PluginReleaseIdentity? release = null
    )
    {
        var pluginId = PluginContractFixtures.PluginId(plugin);
        release ??= Release("1.0.0");
        _ = PluginFeatureId.TryCreate("feature", out var feature);
        _ = PluginHostId.TryCreate(hostId, out var host);
        _ = PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var invocationId);
        _ = PluginCoroutineId.TryCreate(Guid.NewGuid(), out var coroutineId);
        _ = PluginWorkerCancellationId.TryCreate(Guid.NewGuid(), out var cancellationId);
        return new(
            new(pluginId, release),
            feature,
            host,
            new PluginInvocationContext.Channel(new(pluginId, release), host),
            invocationId,
            coroutineId,
            Generation(1),
            PluginWorkerDeadline.From(DateTimeOffset.UtcNow.AddMinutes(1)),
            cancellationId
        );
    }

    private static PluginReleaseIdentity Release(string version)
    {
        _ = SemanticVersion.TryCreate(version, out var semanticVersion);
        _ = PluginGitTag.TryCreate($"v{version}", out var tag);
        return new(semanticVersion, tag);
    }

    private static PluginWorkerGeneration Generation(ulong value)
    {
        _ = PluginWorkerGeneration.TryCreate(value, out var generation);
        return generation;
    }

    private static PluginValue.Map Parameters(
        params (string Name, PluginValue Value)[] parameters
    ) =>
        new([
            .. parameters.Select(parameter => new PluginValueProperty(
                parameter.Name,
                parameter.Value
            )),
        ]);

    private static string Value(PluginSqliteOutcome.Rows rows) =>
        (
            (PluginValue.String)
                rows.Values.ShouldHaveSingleItem().Properties.ShouldHaveSingleItem().Value
        ).Value;

    private sealed class SqlMigrationRunner(PluginPrivateDataStore store)
        : IPluginLifecycleMigrationRunner,
            IPluginLifecycleMigrationSession
    {
        internal bool FailAfterSql { get; set; }

        internal Action? AfterSql { get; set; }

        internal int Invocations { get; private set; }

        public ValueTask<PluginLifecycleMigrationSessionOutcome> StartAsync(
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginLifecycleMigrationSessionOutcome>(
                new PluginLifecycleMigrationSessionOutcome.Started(this)
            );

        public async ValueTask<PluginWorkerInvocationResult> InvokeAsync(
            PluginWorkerInvocationIdentity identity,
            PluginMigrationDescriptor migration,
            PluginValue input,
            CancellationToken cancellationToken
        )
        {
            Invocations++;
            var none = new PluginValue.Map([]);
            _ = await store.ExecuteAsync(
                identity,
                "CREATE TABLE migrated (value TEXT NOT NULL);",
                none,
                cancellationToken
            );
            _ = await store.ExecuteAsync(
                identity,
                "INSERT INTO migrated (value) VALUES ('updated');",
                none,
                cancellationToken
            );
            AfterSql?.Invoke();
            return new(
                FailAfterSql
                    ? new PluginWorkerInvocationOutcome.Failed(
                        new(PluginWorkerFailureCode.EngineFailure, "migration failed")
                    )
                    : new PluginWorkerInvocationOutcome.Returned(new PluginValue.Nil()),
                PluginWorkerInvocationMetrics.Empty,
                []
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TemporaryPrivateDataRoot : IAsyncDisposable
    {
        internal string RootPath { get; } =
            Path.Combine(Path.GetTempPath(), $"blokebot-private-data-{Guid.NewGuid():N}");

        internal string DatabasePath(PluginId pluginId) =>
            Path.Combine(RootPath, pluginId.Value, "private.db");

        internal PluginPrivateDataStore Store() => new(new(RootPath));

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedDispatcher : IPluginHostCallDispatcher
    {
        public ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "Dispatcher should not be called by the test runner."
            );
    }
}
