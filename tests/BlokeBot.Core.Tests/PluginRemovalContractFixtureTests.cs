using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginRemovalContractFixtureTests
{
    [Test]
    public async Task DestructiveRemove_UsesTheCompleteProductionOwnerGraph()
    {
        await using var fixture = await ProductionRemovalFixture.CreateAsync(
            failFirstPackageRemoval: false
        );
        var before = await fixture.ObserveAsync();
        before.ShouldContainEveryOwnedResource();

        var outcome = await fixture.RemoveWithActiveInvocationAsync();

        _ = outcome.ShouldBeOfType<PluginLifecycleCommandOutcome.Removed>();
        var observed = await fixture.ObserveAsync();
        observed.ShouldContainNoPluginOwnedResource();
        observed.GlobalCatalogueMetadataPresent.ShouldBeTrue();
    }

    [Test]
    public async Task DestructiveRemove_RetriesAfterAnOwnerRetainsItsResource()
    {
        await using var fixture = await ProductionRemovalFixture.CreateAsync(
            failFirstPackageRemoval: true
        );
        (await fixture.ObserveAsync()).ShouldContainEveryOwnedResource();

        var failed = await fixture.RemoveAsync();

        var failure = failed.ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>().View;
        failure.Phase.ShouldBe(PluginLifecyclePhase.Faulted);
        failure.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.RemovalFailed);
        var retained = await fixture.ObserveAsync();
        retained.PackagePresent.ShouldBeTrue();
        retained.PluginLifecyclePresent.ShouldBeTrue();

        var retried = await fixture.RemoveAsync();

        _ = retried.ShouldBeOfType<PluginLifecycleCommandOutcome.Removed>();
        var observed = await fixture.ObserveAsync();
        observed.ShouldContainNoPluginOwnedResource();
        observed.GlobalCatalogueMetadataPresent.ShouldBeTrue();
    }

    private sealed class ProductionRemovalFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly string _root;
        private readonly string _privateDatabasePath;
        private readonly IDbContextFactory<BlokeBotDbContext> _database;
        private readonly PluginId _pluginId;
        private readonly PluginFeatureState _featureState;
        private readonly PluginLifecycleCoordinator _coordinator;
        private readonly PluginRuntimeSnapshotRegistry _runtime;
        private readonly PluginDispatchWorkRegistry _dispatchWork;
        private readonly IPluginScheduleStore _schedules;
        private readonly PluginFeatureDeclarationRegistry _declarations;
        private readonly PluginFeatureSnapshotRegistry _features;
        private readonly PluginAutomationCatalogRegistry _automationDefinitions;

        private ProductionRemovalFixture(
            ServiceProvider services,
            string root,
            string privateDatabasePath,
            IDbContextFactory<BlokeBotDbContext> database,
            PluginId pluginId,
            PluginFeatureState featureState,
            PluginLifecycleCoordinator coordinator,
            PluginRuntimeSnapshotRegistry runtime,
            PluginDispatchWorkRegistry dispatchWork,
            IPluginScheduleStore schedules,
            PluginFeatureDeclarationRegistry declarations,
            PluginFeatureSnapshotRegistry features,
            PluginAutomationCatalogRegistry automationDefinitions
        )
        {
            _services = services;
            _root = root;
            _privateDatabasePath = privateDatabasePath;
            _database = database;
            _pluginId = pluginId;
            _featureState = featureState;
            _coordinator = coordinator;
            _runtime = runtime;
            _dispatchWork = dispatchWork;
            _schedules = schedules;
            _declarations = declarations;
            _features = features;
            _automationDefinitions = automationDefinitions;
        }

        internal static async Task<ProductionRemovalFixture> CreateAsync(
            bool failFirstPackageRemoval
        )
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"blokebot-removal-contract-{Guid.NewGuid():N}"
            );
            _ = Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "blokebot.db");
            var packageRoot = Path.Combine(root, "plugin-packages");
            var privateRoot = Path.Combine(root, "plugins");
            var scheduleStore = new PluginScheduleFileStore(
                Path.Combine(root, "plugin-schedules.json")
            );
            var workers = new FixtureLifecycleWorkers();
            var hostCalls = new UnavailableHostCalls();
            var services = new ServiceCollection();
            _ = services.AddLogging();
            _ = services.AddSingleton<IPluginLifecycleWorkerManager>(workers);
            _ = services.AddSingleton<IPluginFeatureReconciler, EmptyPluginFeatureReconciler>();
            _ = services.AddSingleton<IPluginScheduleStore>(scheduleStore);
            _ = services.AddSingleton<IPluginHostCallDispatcher>(hostCalls);
            _ = services.AddSingleton<
                IPluginMarketplaceArchiveTransport,
                UnavailableArchiveTransport
            >();
            _ = services.AddSingleton(new PluginPrivateDataOptions(privateRoot));
            _ = services.AddSingleton(
                new PluginMarketplaceStorageOptions(packageRoot, privateRoot, TimeSpan.FromHours(1))
            );
            _ = services.AddSingleton(
                new PluginMarketplaceRuntimeContext(
                    PluginContractFixtures.CompatibleHost(),
                    hostCalls,
                    NullLogger<PluginWorkerClient>.Instance
                )
            );
            _ = services.AddBlokeBotPersistence(databasePath);
            _ = services.AddBlokeBotAutomations();
            _ = services.AddBlokeBotPluginRuntime();
            _ = services.AddBlokeBotPluginFeatures();
            var provider = services.BuildServiceProvider();
            try
            {
                var database = provider.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>();
                await using (var context = await database.CreateDbContextAsync())
                {
                    _ = await context.Database.EnsureCreatedAsync();
                }

                var runtime = provider.GetRequiredService<PluginRuntimeSnapshotRegistry>();
                var removalOwners = provider.GetServices<IPluginRemovalDataOwner>().ToArray();
                if (failFirstPackageRemoval)
                {
                    var packages = removalOwners
                        .OfType<PluginMarketplacePackageStore>()
                        .ShouldHaveSingleItem();
                    var retrying = new RetryingPackageRemovalOwner(packages);
                    removalOwners =
                    [
                        .. removalOwners.Select(owner =>
                            ReferenceEquals(owner, packages) ? retrying : owner
                        ),
                    ];
                }

                var coordinator = new PluginLifecycleCoordinator(
                    provider.GetRequiredService<IPluginLifecycleStore>(),
                    provider.GetRequiredService<IPluginLifecyclePackageResolver>(),
                    provider.GetServices<IPluginMigrationDataOwner>(),
                    provider.GetServices<IPluginLifecycleActivationPublisher>(),
                    removalOwners,
                    provider.GetRequiredService<IPluginPendingWorkCanceller>(),
                    workers,
                    runtime,
                    provider.GetRequiredService<PluginLifecycleSerialization>(),
                    provider.GetRequiredService<PluginLifecycleOptions>(),
                    provider.GetRequiredService<TimeProvider>(),
                    NullLogger<PluginLifecycleCoordinator>.Instance
                );
                var accepted = PluginManifestToml
                    .Validate(
                        PluginContractFixtures.CompleteManifestToml(),
                        PluginContractFixtures.CompatibleHost()
                    )
                    .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
                    .Manifest;
                var packageOperationId = PluginPackageOperationId.New();
                var packageDirectory = PackageDirectory(
                    packageRoot,
                    accepted.Manifest,
                    packageOperationId
                );
                var package = Package(
                    accepted,
                    packageDirectory,
                    privateRoot,
                    packageOperationId,
                    hostCalls
                );
                var active = (
                    await coordinator.ActivateAsync(
                        PluginLifecycleOperationId.New(),
                        package,
                        CancellationToken.None
                    )
                ).ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>();
                var fence = new PluginLifecycleFence(
                    active.View.OperationId,
                    active.View.Generation
                );
                var featureState = await SeedDatabaseStateAsync(database, accepted.Manifest, fence);
                var features = provider.GetRequiredService<PluginFeatureSnapshotRegistry>();
                features.Hydrate([featureState]);
                var schedules = provider.GetRequiredService<IPluginScheduleStore>();
                await SeedScheduleAsync(schedules, featureState);
                var privateDatabasePath = await SeedPrivateDatabaseAsync(
                    privateRoot,
                    accepted.Manifest.Id
                );
                var receipts = provider.GetRequiredService<IPluginMarketplaceReceiptStore>();
                await receipts.SaveAsync(
                    new(
                        accepted.Manifest.Id,
                        PluginMarketplaceOperationKind.Install,
                        accepted.Manifest.Release,
                        "Activated",
                        null,
                        DateTimeOffset.UtcNow
                    ),
                    CancellationToken.None
                );
                return new(
                    provider,
                    root,
                    privateDatabasePath,
                    database,
                    accepted.Manifest.Id,
                    featureState,
                    coordinator,
                    runtime,
                    provider.GetRequiredService<PluginDispatchWorkRegistry>(),
                    schedules,
                    provider.GetRequiredService<PluginFeatureDeclarationRegistry>(),
                    features,
                    provider.GetRequiredService<PluginAutomationCatalogRegistry>()
                );
            }
            catch
            {
                await provider.DisposeAsync();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
                throw;
            }
        }

        internal ValueTask<PluginLifecycleCommandOutcome> RemoveAsync() =>
            _coordinator.RemoveAsync(
                _pluginId,
                PluginLifecycleOperationId.New(),
                CancellationToken.None
            );

        internal async ValueTask<PluginLifecycleCommandOutcome> RemoveWithActiveInvocationAsync()
        {
            var dispatch = _dispatchWork
                .Admit(_featureState, CancellationToken.None)
                .ShouldBeOfType<PluginDispatchWorkAdmission.Admitted>()
                .Lease;
            var runtime = _runtime
                .Admit(_pluginId, _featureState.Fence, PluginFeatureAdmissionReadiness.Ready)
                .ShouldBeOfType<PluginAdmissionOutcome.Admitted>()
                .Admission;
            try
            {
                var removal = RemoveAsync().AsTask();
                await WaitForCancellationAsync(dispatch.CancellationToken);
                await dispatch.DisposeAsync();
                await runtime.DisposeAsync();
                return await removal;
            }
            finally
            {
                await dispatch.DisposeAsync();
                await runtime.DisposeAsync();
            }
        }

        internal async Task<ObservedRemovalState> ObserveAsync()
        {
            await using var database = await _database.CreateDbContextAsync();
            var databaseState = new OwnedDatabaseState(
                await database.PluginInstallationConfigurations.CountAsync(),
                await database.PluginInstallationSecrets.CountAsync(),
                await database.PluginFeatureConfigurations.CountAsync(),
                await database.PluginFeatureSecrets.CountAsync(),
                await database.PluginFeatureStates.CountAsync(),
                await database.PluginAutomationInstantiations.CountAsync(),
                await database.AutomationFlows.CountAsync(),
                await database.AutomationFlowNodes.CountAsync(),
                await database.AutomationFlowEdges.CountAsync(),
                await database.AutomationFlowRuns.CountAsync(),
                await database.AutomationNodeRuns.CountAsync(),
                await database.AutomationEventReceipts.CountAsync()
            );
            var declaration = _declarations.Current.Declarations.GetValueOrDefault(_pluginId);
            return new(
                await database.PluginLifecycles.AnyAsync(value =>
                    value.PluginId == _pluginId.Value
                ),
                databaseState,
                Directory.Exists(Path.Combine(_root, "plugin-packages", _pluginId.Value)),
                new[]
                {
                    _privateDatabasePath,
                    $"{_privateDatabasePath}-wal",
                    $"{_privateDatabasePath}-shm",
                }.Count(File.Exists),
                (await _schedules.LoadAsync(CancellationToken.None)).Count(schedule =>
                    schedule.Feature.PluginId == _pluginId
                ),
                await database.PluginMarketplaceReceipts.AnyAsync(value =>
                    value.PluginId == _pluginId.Value
                ),
                await database.PluginMarketplaceCatalogEntries.AnyAsync(value =>
                    value.PluginId == _pluginId.Value
                ),
                declaration is not null,
                declaration?.Manifest.Settings.Length ?? 0,
                _features.Current.States.Keys.Any(key => key.PluginId == _pluginId),
                _automationDefinitions.Current.Definitions.Values.Count(definition =>
                    definition.Descriptor.PluginProvenance?.PluginId == _pluginId.Value
                ),
                _runtime.Current.Entries.ContainsKey(_pluginId)
            );
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static PluginLifecyclePackage Package(
            ValidatedPluginManifest manifest,
            string packageDirectory,
            string privateRoot,
            PluginPackageOperationId packageOperationId,
            IPluginHostCallDispatcher hostCalls
        )
        {
            _ = Directory.CreateDirectory(packageDirectory);
            File.WriteAllText(Path.Combine(packageDirectory, "installed.marker"), "installed");
            var prepared = new PreparedPluginWorkerPackage(
                new(
                    new(manifest.Manifest.Id, manifest.Manifest.Release),
                    PluginRuntimeIdentifier.LinuxX64,
                    manifest.Manifest.EntryModule,
                    manifest
                        .Manifest.LuaModules.Select(module => new PluginWorkerLuaModule(
                            module.Id,
                            module.Path
                        ))
                        .ToImmutableArray()
                ),
                packageDirectory
            )
            {
                Manifest = manifest,
            };
            return new(
                prepared.Descriptor.Plugin,
                packageOperationId,
                prepared,
                Path.Combine(privateRoot, manifest.Manifest.Id.Value),
                hostCalls,
                NullLogger<PluginWorkerClient>.Instance
            );
        }

        private static string PackageDirectory(
            string packageRoot,
            PluginManifest manifest,
            PluginPackageOperationId packageOperationId
        ) =>
            Path.Combine(
                packageRoot,
                manifest.Id.Value,
                manifest.Release.DeclaredVersion.Value,
                "selected-tag",
                "operations",
                packageOperationId.Value.ToString("N"),
                "package"
            );

        private static async Task<PluginFeatureState> SeedDatabaseStateAsync(
            IDbContextFactory<BlokeBotDbContext> database,
            PluginManifest manifest,
            PluginLifecycleFence fence
        )
        {
            await using var context = await database.CreateDbContextAsync();
            var now = DateTime.UtcNow;
            var host = new BotHost
            {
                TwitchUserId = "plugin-removal-host",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.Automations,
                CreatedAtUtc = now,
            };
            _ = context.Hosts.Add(host);
            _ = await context.SaveChangesAsync();
            _ = PluginFeatureId.TryCreate("publishing", out var featureId);
            _ = PluginHostId.TryCreate(host.Id, out var hostId);
            _ = PluginFeatureGeneration.TryCreate(1, out var featureGeneration);
            _ = PluginFeatureRevision.TryCreate(1, out var revision);
            var state = new PluginFeatureState(
                new(manifest.Id, featureId, hostId),
                fence,
                featureGeneration,
                new PluginFeatureReadiness.Ready(),
                revision
            );
            var flowId = Guid.NewGuid();
            var sourceNodeId = Guid.NewGuid();
            var actionNodeId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var definitionId = $"plugin.{manifest.Id.Value}.queued-link";
            var provenance = PluginAutomationCatalogRegistry.SerializeProvenance(
                new(
                    manifest.Id.Value,
                    manifest.Release.DeclaredVersion.Value,
                    manifest.Release.Tag.Value,
                    manifest.ManifestVersion,
                    featureId.Value,
                    "queued-link",
                    "definition-hash",
                    fence.OperationId.Value,
                    checked((long)fence.Generation.Value),
                    checked((long)featureGeneration.Value),
                    "publish-links",
                    "template-hash"
                )
            );
            _ = context.PluginInstallationConfigurations.Add(
                new()
                {
                    PluginId = manifest.Id.Value,
                    ValuesJson = "[]",
                    Revision = 0,
                }
            );
            _ = context.PluginInstallationSecrets.Add(
                new()
                {
                    PluginId = manifest.Id.Value,
                    SettingId = "service-token",
                    ProtectedValue = [1],
                }
            );
            _ = context.PluginFeatureConfigurations.Add(
                new()
                {
                    PluginId = manifest.Id.Value,
                    FeatureId = featureId.Value,
                    HostId = host.Id,
                    ValuesJson = "[]",
                    Revision = 0,
                }
            );
            _ = context.PluginFeatureSecrets.Add(
                new()
                {
                    PluginId = manifest.Id.Value,
                    FeatureId = featureId.Value,
                    HostId = host.Id,
                    SettingId = "publish-token",
                    ProtectedValue = [2],
                }
            );
            _ = context.PluginFeatureStates.Add(
                new()
                {
                    PluginId = manifest.Id.Value,
                    FeatureId = featureId.Value,
                    HostId = host.Id,
                    LifecycleOperationId = fence.OperationId.Value,
                    WorkerGeneration = checked((long)fence.Generation.Value),
                    FeatureGeneration = checked((long)featureGeneration.Value),
                    Readiness = PluginFeatureReadinessKind.Ready,
                    Revision = revision.Value,
                }
            );
            var flow = new AutomationFlow
            {
                Id = flowId,
                HostId = host.Id,
                Name = "Plugin-dependent flow",
                SchemaVersion = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Nodes =
                [
                    new()
                    {
                        Id = sourceNodeId,
                        FlowId = flowId,
                        DefinitionId = definitionId,
                        DefinitionSchemaVersion = 1,
                        ConfigurationJson = "{}",
                        InputBindingsJson = "{}",
                        PluginProvenanceJson = provenance,
                    },
                    new()
                    {
                        Id = actionNodeId,
                        FlowId = flowId,
                        DefinitionId = "core.noop",
                        DefinitionSchemaVersion = 1,
                        ConfigurationJson = "{}",
                        InputBindingsJson = "{}",
                    },
                ],
                Edges =
                [
                    new()
                    {
                        Id = Guid.NewGuid(),
                        FlowId = flowId,
                        Kind = PersistedAutomationEdgeKind.Flow,
                        SourceNodeId = sourceNodeId,
                        SourcePortId = "next",
                        TargetNodeId = actionNodeId,
                        TargetPortId = "in",
                    },
                ],
            };
            _ = context.AutomationFlows.Add(flow);
            _ = context.PluginAutomationInstantiations.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    EnableOperationId = Guid.NewGuid(),
                    PluginId = manifest.Id.Value,
                    FeatureId = featureId.Value,
                    HostId = host.Id,
                    TemplateId = "publish-links",
                    PluginVersion = manifest.Release.DeclaredVersion.Value,
                    MutableTag = manifest.Release.Tag.Value,
                    ManifestVersion = manifest.ManifestVersion,
                    TemplateHash = "template-hash",
                    Status = PluginAutomationInstantiationStatus.Completed,
                    FlowId = flowId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
            _ = context.AutomationFlowRuns.Add(
                new()
                {
                    Id = runId,
                    FlowId = flowId,
                    HostId = host.Id,
                    AutomationGeneration = 1,
                    RequiredFeatures = HostFeatureFlags.Automations,
                    ContextSchemaVersion = 1,
                    SourceDefinitionId = definitionId,
                    SourceNodeId = sourceNodeId,
                    SourceOccurrenceId = Guid.NewGuid(),
                    ContextJson = "{}",
                    DefinitionJson = "{}",
                    Status = AutomationFlowRunStatus.Completed,
                    StartedAtUtc = now,
                    CompletedAtUtc = now,
                    NodeRuns =
                    [
                        new()
                        {
                            NodeId = sourceNodeId,
                            Sequence = 1,
                            Status = AutomationNodeRunStatus.Succeeded,
                            AvailableAtUtc = now,
                            StartedAtUtc = now,
                            CompletedAtUtc = now,
                        },
                    ],
                }
            );
            _ = context.AutomationEventReceipts.Add(
                new()
                {
                    HostId = host.Id,
                    SourceDefinitionId = definitionId,
                    ProviderMessageId = "removal-fixture",
                    ClaimedAtUtc = now,
                    ExpiresAtUtc = now.AddMinutes(10),
                }
            );
            _ = context.PluginMarketplaceCatalogState.Add(
                new()
                {
                    Id = 1,
                    SchemaVersion = 1,
                    FetchedAtUtc = now,
                    LastAttemptAtUtc = now,
                }
            );
            _ = context.PluginMarketplaceCatalogEntries.Add(
                new()
                {
                    SnapshotId = 1,
                    PluginId = manifest.Id.Value,
                    DeclaredVersion = manifest.Release.DeclaredVersion.Value,
                    MutableTag = manifest.Release.Tag.Value,
                    Name = manifest.Name,
                    Summary = manifest.Description,
                    Author = "Community",
                    RepositoryUrl = "https://github.com/community/plugins",
                    PackagePath = "plugins/link-queue",
                    CompatibilityBlokeBot = ">=0.13.0 <0.14.0",
                    CompatibilityPluginApi = "1",
                    CompatibilityLua = "5.4",
                }
            );
            _ = await context.SaveChangesAsync();
            return state;
        }

        private static async Task SeedScheduleAsync(
            IPluginScheduleStore schedules,
            PluginFeatureState state
        )
        {
            _ = PluginScheduleHandlerId.TryCreate("publish", out var handlerId);
            await schedules.UpsertAsync(
                new(
                    Guid.NewGuid(),
                    state.Key,
                    new(state.Fence, state.Generation),
                    handlerId,
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    null,
                    new PluginValue.Map([])
                ),
                CancellationToken.None
            );
        }

        private static async Task<string> SeedPrivateDatabaseAsync(
            string privateRoot,
            PluginId pluginId
        )
        {
            var directory = Path.Combine(privateRoot, pluginId.Value);
            _ = Directory.CreateDirectory(directory);
            var databasePath = Path.Combine(directory, "private.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
            }.ToString();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE fixture (value TEXT NOT NULL); INSERT INTO fixture VALUES ('owned');";
                _ = await command.ExecuteNonQueryAsync();
                var wal = await File.ReadAllBytesAsync($"{databasePath}-wal");
                var sharedMemory = await File.ReadAllBytesAsync($"{databasePath}-shm");
                await connection.CloseAsync();
                if (!File.Exists($"{databasePath}-wal"))
                {
                    await File.WriteAllBytesAsync($"{databasePath}-wal", wal);
                }
                if (!File.Exists($"{databasePath}-shm"))
                {
                    await File.WriteAllBytesAsync($"{databasePath}-shm", sharedMemory);
                }
            }
            return databasePath;
        }

        private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            var cancelled = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed record ObservedRemovalState(
        bool PluginLifecyclePresent,
        OwnedDatabaseState Database,
        bool PackagePresent,
        int PrivateDataFiles,
        int ScheduleCount,
        bool ReceiptPresent,
        bool GlobalCatalogueMetadataPresent,
        bool DeclarationPresent,
        int DeclaredSettingCount,
        bool FeatureSnapshotPresent,
        int AutomationDefinitionCount,
        bool RuntimePresent
    )
    {
        internal void ShouldContainEveryOwnedResource()
        {
            PluginLifecyclePresent.ShouldBeTrue();
            Database.ShouldContainEveryOwnedRow();
            PackagePresent.ShouldBeTrue();
            PrivateDataFiles.ShouldBe(3);
            ScheduleCount.ShouldBe(1);
            ReceiptPresent.ShouldBeTrue();
            GlobalCatalogueMetadataPresent.ShouldBeTrue();
            DeclarationPresent.ShouldBeTrue();
            DeclaredSettingCount.ShouldBeGreaterThan(0);
            FeatureSnapshotPresent.ShouldBeTrue();
            AutomationDefinitionCount.ShouldBeGreaterThan(0);
            RuntimePresent.ShouldBeTrue();
        }

        internal void ShouldContainNoPluginOwnedResource()
        {
            PluginLifecyclePresent.ShouldBeFalse();
            Database.ShouldContainNoOwnedRow();
            PackagePresent.ShouldBeFalse();
            PrivateDataFiles.ShouldBe(0);
            ScheduleCount.ShouldBe(0);
            ReceiptPresent.ShouldBeFalse();
            DeclarationPresent.ShouldBeFalse();
            DeclaredSettingCount.ShouldBe(0);
            FeatureSnapshotPresent.ShouldBeFalse();
            AutomationDefinitionCount.ShouldBe(0);
            RuntimePresent.ShouldBeFalse();
        }
    }

    private sealed record OwnedDatabaseState(
        int InstallationConfigurations,
        int InstallationSecrets,
        int FeatureConfigurations,
        int FeatureSecrets,
        int FeatureStates,
        int AutomationInstantiations,
        int AutomationFlows,
        int AutomationNodes,
        int AutomationEdges,
        int AutomationRuns,
        int AutomationNodeRuns,
        int AutomationSourceReceipts
    )
    {
        internal void ShouldContainEveryOwnedRow()
        {
            InstallationConfigurations.ShouldBeGreaterThan(0);
            InstallationSecrets.ShouldBeGreaterThan(0);
            FeatureConfigurations.ShouldBeGreaterThan(0);
            FeatureSecrets.ShouldBeGreaterThan(0);
            FeatureStates.ShouldBeGreaterThan(0);
            AutomationInstantiations.ShouldBeGreaterThan(0);
            AutomationFlows.ShouldBeGreaterThan(0);
            AutomationNodes.ShouldBeGreaterThan(0);
            AutomationEdges.ShouldBeGreaterThan(0);
            AutomationRuns.ShouldBeGreaterThan(0);
            AutomationNodeRuns.ShouldBeGreaterThan(0);
            AutomationSourceReceipts.ShouldBeGreaterThan(0);
        }

        internal void ShouldContainNoOwnedRow()
        {
            InstallationConfigurations.ShouldBe(0);
            InstallationSecrets.ShouldBe(0);
            FeatureConfigurations.ShouldBe(0);
            FeatureSecrets.ShouldBe(0);
            FeatureStates.ShouldBe(0);
            AutomationInstantiations.ShouldBe(0);
            AutomationFlows.ShouldBe(0);
            AutomationNodes.ShouldBe(0);
            AutomationEdges.ShouldBe(0);
            AutomationRuns.ShouldBe(0);
            AutomationNodeRuns.ShouldBe(0);
            AutomationSourceReceipts.ShouldBe(0);
        }
    }

    private sealed class RetryingPackageRemovalOwner(IPluginRemovalDataOwner package)
        : IPluginRemovalDataOwner
    {
        private bool _failed;

        public ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
            PluginRemovalContext context,
            CancellationToken cancellationToken
        )
        {
            if (!_failed)
            {
                _failed = true;
                return ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
                    new PluginLifecycleOwnerOutcome.Failed(
                        PluginLifecycleOwnerFailureCode.Failed,
                        null
                    )
                );
            }
            return package.RemoveAsync(context, cancellationToken);
        }
    }

    private sealed class FixtureLifecycleWorkers : IPluginLifecycleWorkerManager
    {
        public ValueTask<PluginLifecycleWorkerStartOutcome> ValidateAsync(
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        ) => Started(PluginWorkerMode.Staging);

        public ValueTask<PluginLifecycleWorkerStartOutcome> StartAdmittedAsync(
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        ) => Started(PluginWorkerMode.Admitted);

        private static ValueTask<PluginLifecycleWorkerStartOutcome> Started(
            PluginWorkerMode mode
        ) =>
            ValueTask.FromResult<PluginLifecycleWorkerStartOutcome>(
                new PluginLifecycleWorkerStartOutcome.Started(new FixtureWorker(mode))
            );
    }

    private sealed class FixtureWorker(PluginWorkerMode mode) : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode { get; } = mode;

        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>().Task;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnavailableArchiveTransport : IPluginMarketplaceArchiveTransport
    {
        public ValueTask<PluginMarketplaceArchiveDownload> DownloadAsync(
            Uri repository,
            PluginGitTag tag,
            string destination,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginMarketplaceArchiveDownload>(
                new PluginMarketplaceArchiveDownload.Failed()
            );
    }

    private sealed class UnavailableHostCalls : IPluginHostCallDispatcher
    {
        public ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginHostCallOutcome>(
                new PluginHostCallOutcome.Failed(
                    new(PluginHostFailureCode.Unavailable, "Unavailable in removal fixture.")
                )
            );
    }
}
