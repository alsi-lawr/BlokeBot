using System.Text;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ReferencePluginLifecycleSmokeTests
{
    private const string _packagePathVariable = "BLOKEBOT_REFERENCE_PLUGIN_PATH";

    [Test]
    [Explicit]
    public async Task ExactLocalPackage_ComposesTheReferenceLifecycleWithoutExternalCalls()
    {
        var packagePath = Environment.GetEnvironmentVariable(_packagePathVariable);
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new InvalidOperationException(
                $"Set {_packagePathVariable} to the exact community.link-queue package directory."
            );
        }

        await using var fixture = await ReferenceLifecycleFixture.CreateAsync(packagePath);

        await fixture.RunAsync();
    }

    private sealed class ReferenceLifecycleFixture : IAsyncDisposable
    {
        private const string _endpoint = "https://metadata.example.invalid/enrich";
        private readonly string _root;
        private readonly string _packageStateRoot;
        private readonly string? _previousWorkerPath;
        private readonly IReadOnlyList<PluginPackageEntry> _packageEntries;
        private readonly PluginHostCompatibilityTarget _target;
        private readonly ServiceProvider _services;
        private readonly IDbContextFactory<BlokeBotDbContext> _database;
        private readonly PluginMarketplacePackageStore _packages;
        private readonly IPluginLifecycleCoordinator _lifecycle;
        private readonly PluginRuntimeSnapshotRegistry _runtime;
        private readonly PluginFeatureDeclarationRegistry _declarations;
        private readonly PluginFeatureSnapshotRegistry _features;
        private readonly IPluginFeatureStore _featureStore;
        private readonly PluginFeatureManager _featureManager;
        private readonly string _privateDatabasePath;
        private readonly IPluginMarketplaceReceiptStore _receipts;
        private readonly IPluginScheduleStore _scheduleStore;
        private readonly RestartingLifecycleWorkers _workers;
        private readonly ControlledMigrationOwner _controlledMigration;
        private readonly FixtureHttpModule _http;
        private readonly FixtureSchedulesModule _schedules;
        private readonly PluginHostModuleCatalog _hostCalls;
        private readonly PluginId _pluginId;
        private readonly PluginInstallationIdentity _installation;
        private readonly PluginHostId _firstHost;
        private readonly PluginHostId _secondHost;

        private ReferenceLifecycleFixture(
            string root,
            string packageStateRoot,
            string? previousWorkerPath,
            IReadOnlyList<PluginPackageEntry> packageEntries,
            PluginHostCompatibilityTarget target,
            ServiceProvider services,
            IDbContextFactory<BlokeBotDbContext> database,
            PluginMarketplacePackageStore packages,
            IPluginLifecycleCoordinator lifecycle,
            PluginRuntimeSnapshotRegistry runtime,
            PluginFeatureDeclarationRegistry declarations,
            PluginFeatureSnapshotRegistry features,
            IPluginFeatureStore featureStore,
            PluginFeatureManager featureManager,
            string privateDatabasePath,
            IPluginMarketplaceReceiptStore receipts,
            IPluginScheduleStore scheduleStore,
            RestartingLifecycleWorkers workers,
            ControlledMigrationOwner controlledMigration,
            FixtureHttpModule http,
            FixtureSchedulesModule schedules,
            PluginHostModuleCatalog hostCalls,
            PluginId pluginId,
            PluginInstallationIdentity installation,
            PluginHostId firstHost,
            PluginHostId secondHost
        )
        {
            _root = root;
            _packageStateRoot = packageStateRoot;
            _previousWorkerPath = previousWorkerPath;
            _packageEntries = packageEntries;
            _target = target;
            _services = services;
            _database = database;
            _packages = packages;
            _lifecycle = lifecycle;
            _runtime = runtime;
            _declarations = declarations;
            _features = features;
            _featureStore = featureStore;
            _featureManager = featureManager;
            _privateDatabasePath = privateDatabasePath;
            _receipts = receipts;
            _scheduleStore = scheduleStore;
            _workers = workers;
            _controlledMigration = controlledMigration;
            _http = http;
            _schedules = schedules;
            _hostCalls = hostCalls;
            _pluginId = pluginId;
            _installation = installation;
            _firstHost = firstHost;
            _secondHost = secondHost;
        }

        internal static async Task<ReferenceLifecycleFixture> CreateAsync(string packagePath)
        {
            var fullPackagePath = Path.GetFullPath(packagePath);
            if (!File.Exists(Path.Combine(fullPackagePath, PluginPackage.ManifestPath)))
            {
                throw new InvalidOperationException(
                    $"Reference package manifest was not found at '{fullPackagePath}'."
                );
            }
            if (!File.Exists(Path.Combine(fullPackagePath, "tests.toml")))
            {
                throw new InvalidOperationException(
                    $"Reference package tests.toml was not found at '{fullPackagePath}'."
                );
            }
            var loaded = await PublishedPluginExampleSourceLoader.LoadForTestsAsync(
                fullPackagePath,
                CancellationToken.None
            );
            var example = loaded
                .ShouldBeOfType<PublishedPluginExampleSourceLoadOutcome.Loaded>()
                .Examples.ShouldHaveSingleItem();
            PluginRuntimeIdentifierResolver
                .TryResolveCurrent(out var runtimeIdentifier)
                .ShouldBeTrue();
            var target = PluginAuthoringContract.Current.Target(runtimeIdentifier);
            var accepted = PluginPackageValidator
                .Validate(example.Package, target)
                .ShouldBeOfType<PluginPackageValidationOutcome.Accepted>();
            var installation = new PluginInstallationIdentity(
                accepted.Manifest.Manifest.Id,
                accepted.Manifest.Manifest.Release
            );
            AssertRepositoryLayout(fullPackagePath, accepted.Manifest.Manifest);

            var root = Path.Combine(
                Path.GetTempPath(),
                $"blokebot-reference-plugin-{Guid.NewGuid():N}"
            );
            var packageStateRoot = Path.Combine(root, "packages");
            var privateStateRoot = Path.Combine(root, "private");
            _ = Directory.CreateDirectory(root);
            var workerPath = FindWorkerPath();
            var previousWorkerPath = Environment.GetEnvironmentVariable(
                PluginWorkerDiscovery.WorkerPathEnvironmentVariable
            );
            Environment.SetEnvironmentVariable(
                PluginWorkerDiscovery.WorkerPathEnvironmentVariable,
                workerPath
            );
            var workers = new RestartingLifecycleWorkers();
            var controlledMigration = new ControlledMigrationOwner();
            var reconciler = new ReferenceFeatureReconciler();
            var hostCalls = new DelegatingHostCalls();
            var privateOptions = new PluginPrivateDataOptions(privateStateRoot);
            var services = new ServiceCollection();
            _ = services.AddLogging();
            _ = services.AddDataProtection().UseEphemeralDataProtectionProvider();
            _ = services.AddSingleton(TimeProvider.System);
            _ = services.AddSingleton(
                new PluginLifecycleOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero)
            );
            _ = services.AddSingleton<IPluginLifecycleWorkerManager>(workers);
            _ = services.AddSingleton<IPluginMigrationDataOwner>(controlledMigration);
            _ = services.AddSingleton<IPluginFeatureReconciler>(reconciler);
            _ = services.AddSingleton<IPluginCoreDependencyChecker, AvailableCoreDependencies>();
            _ = services.AddSingleton<IPluginHostCallDispatcher>(hostCalls);
            _ = services.AddSingleton<
                IPluginMarketplaceArchiveTransport,
                UnavailableArchiveTransport
            >();
            _ = services.AddSingleton(privateOptions);
            _ = services.AddSingleton(
                new PluginMarketplaceStorageOptions(
                    packageStateRoot,
                    privateStateRoot,
                    TimeSpan.FromHours(1)
                )
            );
            _ = services.AddSingleton(
                new PluginMarketplaceRuntimeContext(
                    target,
                    hostCalls,
                    NullLogger<PluginWorkerClient>.Instance
                )
            );
            _ = services.AddSingleton<IPluginScheduleStore>(
                new PluginScheduleFileStore(Path.Combine(root, "schedules.json"))
            );
            _ = services.AddBlokeBotPersistence(Path.Combine(root, "blokebot.db"));
            _ = services.AddBlokeBotAutomations();
            _ = services.AddBlokeBotPluginRuntime();
            _ = services.AddBlokeBotPluginFeatures();
            var provider = services.BuildServiceProvider();
            try
            {
                var database = provider.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>();
                var hosts = await SeedDatabaseAsync(
                    database,
                    fullPackagePath,
                    accepted.Manifest.Manifest
                );
                var runtime = provider.GetRequiredService<PluginRuntimeSnapshotRegistry>();
                var declarations = provider.GetRequiredService<PluginFeatureDeclarationRegistry>();
                var features = provider.GetRequiredService<PluginFeatureSnapshotRegistry>();
                var featureStore = provider.GetRequiredService<IPluginFeatureStore>();
                var privateData = provider.GetRequiredService<PluginPrivateDataStore>();
                var http = new FixtureHttpModule(_endpoint);
                var schedules = new FixtureSchedulesModule();
                var catalog = new PluginHostModuleCatalog(
                    [
                        new PluginContextHostModule(),
                        new PluginSettingsHostModule(
                            featureStore,
                            declarations,
                            provider.GetRequiredService<IPluginSecretProtector>()
                        ),
                        new PluginStorageHostModule(privateData),
                        http,
                        schedules,
                    ],
                    new PluginFeatureAdmissionService(features, runtime),
                    NullLogger<PluginHostModuleCatalog>.Instance
                );
                hostCalls.Target = catalog;
                return new(
                    root,
                    packageStateRoot,
                    previousWorkerPath,
                    example.Package,
                    target,
                    provider,
                    database,
                    provider.GetRequiredService<PluginMarketplacePackageStore>(),
                    provider.GetRequiredService<IPluginLifecycleCoordinator>(),
                    runtime,
                    declarations,
                    features,
                    featureStore,
                    provider.GetRequiredService<PluginFeatureManager>(),
                    Path.Combine(privateStateRoot, installation.PluginId.Value, "private.db"),
                    provider.GetRequiredService<IPluginMarketplaceReceiptStore>(),
                    provider.GetRequiredService<IPluginScheduleStore>(),
                    workers,
                    controlledMigration,
                    http,
                    schedules,
                    catalog,
                    accepted.Manifest.Manifest.Id,
                    installation,
                    hosts.First,
                    hosts.Second
                );
            }
            catch
            {
                Environment.SetEnvironmentVariable(
                    PluginWorkerDiscovery.WorkerPathEnvironmentVariable,
                    previousWorkerPath
                );
                await provider.DisposeAsync();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
                throw;
            }
        }

        internal async Task RunAsync()
        {
            var initialPackage = await MaterializeAsync(PluginPackageOperationId.New());
            var activation = Succeeded(
                await _lifecycle.ActivateAsync(
                    PluginLifecycleOperationId.New(),
                    initialPackage,
                    CancellationToken.None
                ),
                "initial activation"
            );
            activation.View.Installation.ShouldBe(_installation);
            _declarations.Current.Declarations.ShouldContainKey(_pluginId);
            await _receipts.SaveAsync(
                new(
                    _pluginId,
                    PluginMarketplaceOperationKind.Install,
                    _installation.Release,
                    "Activated",
                    null,
                    DateTimeOffset.UtcNow
                ),
                CancellationToken.None
            );

            await ExerciseFeaturesAsync(initialPackage);
            await ExerciseAutomaticRecoveryAsync();

            var replacementPackage = await MaterializeAsync(PluginPackageOperationId.New());
            _controlledMigration.FailNext = true;
            var replacement = (
                await _lifecycle.ReplaceAsync(
                    PluginLifecycleOperationId.New(),
                    replacementPackage,
                    CancellationToken.None
                )
            ).ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>();
            replacement.View.Phase.ShouldBe(PluginLifecyclePhase.Faulted);
            replacement.View.LatestOutcome.FailureCode.ShouldBe(
                PluginLifecycleFailureCode.MigrationFailed
            );
            var admittedStarts = _workers.AdmittedStarts;
            _workers.Current.ShouldNotBeNull().Disposed.ShouldBeTrue();

            await _lifecycle.RecoverAsync(CancellationToken.None);
            await Task.Delay(50);

            _workers.AdmittedStarts.ShouldBe(admittedStarts);
            _runtime.Current.Entries[_pluginId].Phase.ShouldBe(PluginLifecyclePhase.Faulted);

            var removed = await _lifecycle.RemoveAsync(
                _pluginId,
                PluginLifecycleOperationId.New(),
                CancellationToken.None
            );
            _ = removed.ShouldBeOfType<PluginLifecycleCommandOutcome.Removed>();
            await AssertRemovedAsync();

            var reinstalledPackage = await MaterializeAsync(PluginPackageOperationId.New());
            var reinstall = (
                await _lifecycle.ActivateAsync(
                    PluginLifecycleOperationId.New(),
                    reinstalledPackage,
                    CancellationToken.None
                )
            ).ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>();
            reinstall.View.Installation.ShouldBe(_installation);
            await AssertFreshReinstallAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            Environment.SetEnvironmentVariable(
                PluginWorkerDiscovery.WorkerPathEnvironmentVariable,
                _previousWorkerPath
            );
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private async Task ExerciseFeaturesAsync(PluginLifecyclePackage package)
        {
            var collectionFirst = Key("collection", _firstHost);
            var collectionSecond = Key("collection", _secondHost);
            var publishing = Key("publishing", _firstHost);
            (
                await _featureStore.LoadFeatureStateAsync(collectionFirst, CancellationToken.None)
            ).ShouldBeNull();
            (
                await _featureStore.LoadFeatureStateAsync(publishing, CancellationToken.None)
            ).ShouldBeNull();
            await SaveInstallationConfigurationAsync();
            await SavePublishingConfigurationAsync(publishing);

            var firstCollection = (
                await _featureManager.EnableAsync(collectionFirst, CancellationToken.None)
            )
                .ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>()
                .State;
            _ = firstCollection.Readiness.ShouldBeOfType<PluginFeatureReadiness.Ready>();
            var firstFlow = await FlowIdAsync(_firstHost);
            _ = (
                await _featureManager.EnableAsync(collectionFirst, CancellationToken.None)
            ).ShouldBeOfType<PluginFeatureEnableOutcome.AlreadyEnabled>();
            (await FlowIdAsync(_firstHost)).ShouldBe(firstFlow);

            _ = (
                await _featureManager.DisableAsync(collectionFirst, CancellationToken.None)
            ).ShouldBeOfType<PluginFeatureDisableOutcome.Disabled>();
            _ = (
                await _featureManager.EnableAsync(collectionFirst, CancellationToken.None)
            ).ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>();
            (await FlowIdAsync(_firstHost)).ShouldBe(firstFlow);

            _ = (
                await _featureManager.DisableAsync(collectionFirst, CancellationToken.None)
            ).ShouldBeOfType<PluginFeatureDisableOutcome.Disabled>();
            await DeleteFlowAsync(firstFlow);
            var recreatedCollection = (
                await _featureManager.EnableAsync(collectionFirst, CancellationToken.None)
            )
                .ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>()
                .State;
            var recreatedFlow = await FlowIdAsync(_firstHost);
            recreatedFlow.ShouldNotBe(firstFlow);

            var secondCollection = (
                await _featureManager.EnableAsync(collectionSecond, CancellationToken.None)
            )
                .ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>()
                .State;
            var pendingPublishing = (
                await _featureManager.EnableAsync(publishing, CancellationToken.None)
            )
                .ShouldBeOfType<PluginFeatureEnableOutcome.Enabled>()
                .State;
            _ =
                pendingPublishing.Readiness.ShouldBeOfType<PluginFeatureReadiness.EnabledDegraded>();
            var readyPublishing = (
                await _featureManager.RetryAsync(publishing, CancellationToken.None)
            )
                .ShouldBeOfType<PluginFeatureReconciliationApplyOutcome.Applied>()
                .State;
            _ = readyPublishing.Readiness.ShouldBeOfType<PluginFeatureReadiness.Ready>();
            readyPublishing.Generation.ShouldBe(pendingPublishing.Generation);

            await ExerciseLivePackageAsync(
                package,
                recreatedCollection,
                secondCollection,
                readyPublishing
            );
            await SeedScheduleAndHistoryAsync(readyPublishing, recreatedFlow);
        }

        private async Task ExerciseLivePackageAsync(
            PluginLifecyclePackage package,
            PluginFeatureState firstCollection,
            PluginFeatureState secondCollection,
            PluginFeatureState publishing
        )
        {
            var started = await PluginWorkerClient.StartAsync(
                new(
                    package.PreparedPackage,
                    Path.Combine(_root, "live-worker"),
                    PluginWorkerMode.Admitted,
                    _hostCalls,
                    NullLogger<PluginWorkerClient>.Instance,
                    WorkerExecutable()
                ),
                CancellationToken.None
            );
            await using var worker = started
                .ShouldBeOfType<PluginWorkerStartOutcome.Started>()
                .Client;

            await StoreAsync(worker, firstCollection, "https://one.example.invalid/link");
            await StoreAsync(worker, firstCollection, "https://two.example.invalid/link");
            await StoreAsync(worker, secondCollection, "https://other.example.invalid/link");
            var schedule = await worker.InvokeAsync(
                Identity(
                    publishing,
                    new PluginInvocationContext.Channel(package.Installation, publishing.Key.HostId)
                ),
                new PluginLiveInvocation.HostAction(
                    Module("queue"),
                    Operation("configure_schedule"),
                    new PluginValue.Nil()
                ),
                CancellationToken.None
            );
            _ = schedule.Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>();
            await RenderAndParseAsync(worker, firstCollection, "queue-management", "render_queue");
            await RenderAndParseAsync(
                worker,
                publishing,
                "publishing-management",
                "render_publishing"
            );

            _http.Attempts.ShouldBe(3);
            _http.AllRequestsUsedConfiguredEndpoint.ShouldBeTrue();
            _http.AllRequestsHadProtectedAuthorization.ShouldBeTrue();
            _schedules.RecurringCalls.ShouldBe(1);
            _schedules.LastIntervalSeconds.ShouldBe(300);
            await AssertPartitionedRowsAsync();
        }

        private async Task RenderAndParseAsync(
            PluginWorkerClient worker,
            PluginFeatureState state,
            string pageIdValue,
            string operationValue
        )
        {
            var pageId = Page(pageIdValue);
            var session = PageSessionId();
            var outcome = await worker.InvokeAsync(
                Identity(
                    state,
                    new PluginInvocationContext.Page(
                        _installation,
                        state.Key.HostId,
                        pageId,
                        session
                    )
                ),
                new PluginLiveInvocation.Page(
                    Module("pages"),
                    Operation(operationValue),
                    new PluginValue.Map([
                        new("version", new PluginValue.Number(1)),
                        new("hostId", new PluginValue.Number(state.Key.HostId.Value)),
                        new("sessionId", new PluginValue.String(session.Value.ToString("D"))),
                    ])
                ),
                CancellationToken.None
            );
            var returned = outcome.Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>();
            var feature = _declarations
                .Current.Declarations[_pluginId]
                .FindFeature(state.Key.FeatureId)
                .ShouldNotBeNull();
            _ = PluginPageDocumentParser
                .Parse(returned.Value, feature)
                .ShouldBeOfType<PluginPageDocumentParseOutcome.Parsed>();
        }

        private async Task StoreAsync(
            PluginWorkerClient worker,
            PluginFeatureState state,
            string url
        )
        {
            var definition = Definition("store-submission");
            var outcome = await worker.InvokeAsync(
                Identity(
                    state,
                    new PluginInvocationContext.Automation(
                        _installation,
                        state.Key.HostId,
                        state.Key.FeatureId,
                        definition,
                        AutomationInvocationId()
                    )
                ),
                new PluginLiveInvocation.Automation(
                    Module("queue"),
                    Operation("store_submission"),
                    definition,
                    PluginAutomationDefinitionKind.Action,
                    new PluginValue.Map([
                        new(
                            "inputs",
                            new PluginValue.Map([new("url", new PluginValue.String(url))])
                        ),
                        new("configuration", new PluginValue.Map([])),
                    ])
                ),
                CancellationToken.None
            );
            _ = outcome.Outcome.ShouldBeOfType<PluginWorkerInvocationOutcome.Returned>();
        }

        private async Task ExerciseAutomaticRecoveryAsync()
        {
            _workers.AdmittedStarts.ShouldBe(1);
            var failedWorker = _workers.Current.ShouldNotBeNull();

            failedWorker.Terminate(
                new(PluginWorkerFailureCode.WorkerExited, "Reference worker exited.")
            );
            await WaitUntilAsync(() => _workers.AdmittedStarts == 2);
            await WaitUntilAsync(() =>
                _runtime.Current.Entries.TryGetValue(_pluginId, out var entry)
                && entry.Phase == PluginLifecyclePhase.Active
            );

            failedWorker.Disposed.ShouldBeTrue();
            _workers.Current.ShouldNotBeSameAs(failedWorker);
        }

        private async Task SaveInstallationConfigurationAsync()
        {
            var owner = new PluginConfigurationOwner.Installation(_pluginId);
            var current = (
                await _featureManager.LoadConfigurationAsync(owner, CancellationToken.None)
            ).ShouldBeOfType<PluginConfigurationLoadOutcome.Loaded>();
            PluginSecretPlaintext
                .TryCreate("reference-fixture-protected-token", 512, out var secret)
                .ShouldBeTrue();
            var saved = await _featureManager.SaveConfigurationAsync(
                new(
                    owner,
                    current.Configuration.Revision,
                    Values(Entry("metadata-endpoint", new PluginSettingValue.Text(_endpoint))),
                    [new(Setting("metadata-token"), new PluginSecretUpdate.Replace(secret))]
                ),
                CancellationToken.None
            );
            _ = saved.ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();
        }

        private async Task SavePublishingConfigurationAsync(PluginFeatureKey key)
        {
            var owner = new PluginConfigurationOwner.Feature(key);
            var current = (
                await _featureManager.LoadConfigurationAsync(owner, CancellationToken.None)
            ).ShouldBeOfType<PluginConfigurationLoadOutcome.Loaded>();
            var saved = await _featureManager.SaveConfigurationAsync(
                new(
                    owner,
                    current.Configuration.Revision,
                    Values(Entry("publish-interval", new PluginSettingValue.Duration(300))),
                    []
                ),
                CancellationToken.None
            );
            _ = saved.ShouldBeOfType<PluginConfigurationSaveOutcome.Saved>();
        }

        private async Task SeedScheduleAndHistoryAsync(
            PluginFeatureState publishing,
            Guid collectionFlowId
        )
        {
            await _scheduleStore.UpsertAsync(
                new(
                    Guid.NewGuid(),
                    publishing.Key,
                    new(publishing.Fence, publishing.Generation),
                    ScheduleHandler("publish-approved"),
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    300,
                    new PluginValue.Map([])
                ),
                CancellationToken.None
            );
            await using var db = await _database.CreateDbContextAsync();
            var flow = await db
                .AutomationFlows.Include(static candidate => candidate.Nodes)
                .SingleAsync(candidate => candidate.Id == collectionFlowId);
            var source = flow.Nodes.First();
            var now = DateTime.UtcNow;
            _ = db.AutomationFlowRuns.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    FlowId = flow.Id,
                    HostId = flow.HostId,
                    AutomationGeneration = 1,
                    RequiredFeatures = HostFeatureFlags.Automations,
                    ContextSchemaVersion = 1,
                    SourceDefinitionId = source.DefinitionId,
                    SourceNodeId = source.Id,
                    SourceOccurrenceId = Guid.NewGuid(),
                    ContextJson = "{}",
                    DefinitionJson = "{}",
                    Status = AutomationFlowRunStatus.Completed,
                    StartedAtUtc = now,
                    CompletedAtUtc = now,
                }
            );
            _ = db.AutomationEventReceipts.Add(
                new()
                {
                    HostId = flow.HostId,
                    SourceDefinitionId = PluginAutomationCatalogRegistry
                        .DefinitionId(_pluginId, Definition("link-submitted"))
                        .Value,
                    ProviderMessageId = "reference-lifecycle-smoke",
                    ClaimedAtUtc = now,
                    ExpiresAtUtc = now.AddMinutes(5),
                }
            );
            _ = await db.SaveChangesAsync();
        }

        private async Task AssertPartitionedRowsAsync()
        {
            await using var connection = new SqliteConnection(
                $"Data Source={_privateDatabasePath};Pooling=False"
            );
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT host_id, COUNT(*), SUM(CASE WHEN url LIKE 'https://%' THEN 1 ELSE 0 END) FROM community_links GROUP BY host_id ORDER BY host_id";
            await using var rows = await command.ExecuteReaderAsync();
            (await rows.ReadAsync()).ShouldBeTrue();
            rows.GetInt64(0).ShouldBe(_firstHost.Value);
            rows.GetInt64(1).ShouldBe(2);
            rows.GetInt64(2).ShouldBe(2);
            (await rows.ReadAsync()).ShouldBeTrue();
            rows.GetInt64(0).ShouldBe(_secondHost.Value);
            rows.GetInt64(1).ShouldBe(1);
            rows.GetInt64(2).ShouldBe(1);
            (await rows.ReadAsync()).ShouldBeFalse();
        }

        private async Task AssertRemovedAsync()
        {
            await using var db = await _database.CreateDbContextAsync();
            (await db.PluginLifecycles.CountAsync()).ShouldBe(0);
            (await db.PluginInstallationConfigurations.CountAsync()).ShouldBe(0);
            (await db.PluginInstallationSecrets.CountAsync()).ShouldBe(0);
            (await db.PluginFeatureConfigurations.CountAsync()).ShouldBe(0);
            (await db.PluginFeatureSecrets.CountAsync()).ShouldBe(0);
            (await db.PluginFeatureStates.CountAsync()).ShouldBe(0);
            (await db.PluginAutomationInstantiations.CountAsync()).ShouldBe(0);
            (await db.AutomationFlows.CountAsync()).ShouldBe(0);
            (await db.AutomationFlowNodes.CountAsync()).ShouldBe(0);
            (await db.AutomationFlowEdges.CountAsync()).ShouldBe(0);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationEventReceipts.CountAsync()).ShouldBe(0);
            (await db.PluginMarketplaceReceipts.CountAsync()).ShouldBe(0);
            (
                await db.PluginMarketplaceCatalogEntries.CountAsync(candidate =>
                    candidate.PluginId == _pluginId.Value
                )
            ).ShouldBe(1);
            Directory.Exists(Path.Combine(_packageStateRoot, _pluginId.Value)).ShouldBeFalse();
            File.Exists(_privateDatabasePath).ShouldBeFalse();
            _declarations.Current.Declarations.ShouldNotContainKey(_pluginId);
            _features.Current.States.Keys.ShouldNotContain(key => key.PluginId == _pluginId);
            _runtime.Current.Entries.ShouldNotContainKey(_pluginId);
            (await _scheduleStore.LoadAsync(CancellationToken.None)).ShouldNotContain(entry =>
                entry.Feature.PluginId == _pluginId
            );
        }

        private async Task AssertFreshReinstallAsync()
        {
            await using var db = await _database.CreateDbContextAsync();
            (await db.PluginLifecycles.CountAsync()).ShouldBe(1);
            (await db.PluginInstallationConfigurations.CountAsync()).ShouldBe(0);
            (await db.PluginInstallationSecrets.CountAsync()).ShouldBe(0);
            (await db.PluginFeatureConfigurations.CountAsync()).ShouldBe(0);
            (await db.PluginFeatureSecrets.CountAsync()).ShouldBe(0);
            (await db.PluginFeatureStates.CountAsync()).ShouldBe(0);
            (await db.PluginAutomationInstantiations.CountAsync()).ShouldBe(0);
            (await db.AutomationFlows.CountAsync()).ShouldBe(0);
            (
                await db.PluginMarketplaceCatalogEntries.CountAsync(candidate =>
                    candidate.PluginId == _pluginId.Value
                )
            ).ShouldBe(1);
            _declarations.Current.Declarations.ShouldContainKey(_pluginId);
            File.Exists(_privateDatabasePath).ShouldBeTrue();
            await using var connection = new SqliteConnection(
                $"Data Source={_privateDatabasePath};Pooling=False"
            );
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM community_links";
            ((long)(await command.ExecuteScalarAsync()).ShouldNotBeNull()).ShouldBe(0);
        }

        private async Task<PluginLifecyclePackage> MaterializeAsync(
            PluginPackageOperationId operationId
        )
        {
            var destination = PackageDirectory(operationId);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            _ = (
                await PluginWorkerPackageMaterializer.MaterializeAsync(
                    _packageEntries,
                    _target,
                    destination,
                    CancellationToken.None
                )
            ).ShouldBeOfType<PluginPackageMaterializationOutcome.Prepared>();
            return (
                await _packages.ResolveAsync(_installation, operationId, CancellationToken.None)
            )
                .ShouldBeOfType<PluginLifecyclePackageResolution.Available>()
                .Package;
        }

        private string PackageDirectory(PluginPackageOperationId operationId) =>
            Path.Combine(
                _packageStateRoot,
                _pluginId.Value,
                _installation.Release.DeclaredVersion.Value,
                EncodeTag(_installation.Release.Tag.Value),
                "operations",
                operationId.Value.ToString("N"),
                "package"
            );

        private async Task<Guid> FlowIdAsync(PluginHostId host)
        {
            await using var db = await _database.CreateDbContextAsync();
            return (
                await db
                    .PluginAutomationInstantiations.Where(candidate =>
                        candidate.PluginId == _pluginId.Value
                        && candidate.FeatureId == "collection"
                        && candidate.HostId == host.Value
                        && candidate.Status == PluginAutomationInstantiationStatus.Completed
                    )
                    .OrderByDescending(static candidate => candidate.UpdatedAtUtc)
                    .Select(static candidate => candidate.FlowId)
                    .FirstAsync()
            ).ShouldNotBeNull();
        }

        private async Task DeleteFlowAsync(Guid flowId)
        {
            await using var db = await _database.CreateDbContextAsync();
            _ = await db
                .AutomationFlows.Where(candidate => candidate.Id == flowId)
                .ExecuteDeleteAsync();
        }

        private PluginWorkerInvocationIdentity Identity(
            PluginFeatureState state,
            PluginInvocationContext context
        )
        {
            PluginActivationOperationId
                .TryCreate(state.Fence.OperationId.Value, out var operation)
                .ShouldBeTrue();
            PluginFeatureActivationGeneration
                .TryCreate(state.Generation.Value, out var featureGeneration)
                .ShouldBeTrue();
            PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var invocationId).ShouldBeTrue();
            PluginCoroutineId.TryCreate(Guid.NewGuid(), out var coroutineId).ShouldBeTrue();
            PluginWorkerCancellationId
                .TryCreate(Guid.NewGuid(), out var cancellationId)
                .ShouldBeTrue();
            return new(
                _installation,
                state.Key.FeatureId,
                state.Key.HostId,
                context,
                invocationId,
                coroutineId,
                state.Fence.Generation,
                PluginWorkerDeadline.From(DateTimeOffset.UtcNow.AddSeconds(10)),
                cancellationId,
                new(operation, state.Fence.Generation, featureGeneration)
            );
        }

        private PluginFeatureKey Key(string feature, PluginHostId host) =>
            new(_pluginId, Feature(feature), host);

        private static PluginWorkerExecutable WorkerExecutable() =>
            PluginWorkerDiscovery.Discover() is PluginWorkerDiscoveryOutcome.Found found
                ? found.Executable
                : throw new InvalidOperationException(
                    "The focused reference lifecycle worker executable was not found."
                );

        private static string FindWorkerPath()
        {
            var configured = Environment.GetEnvironmentVariable(
                PluginWorkerDiscovery.WorkerPathEnvironmentVariable
            );
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return Path.GetFullPath(configured);
            }
            for (
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                directory is not null;
                directory = directory.Parent
            )
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "tools",
                    "BlokeBot.PluginHarness",
                    "bin",
                    "Release",
                    "net10.0",
                    "plugin-worker",
                    "BlokeBot.PluginWorker.dll"
                );
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException(
                "Build the Release PluginHarness before the explicit lifecycle smoke."
            );
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    throw new TimeoutException("Reference lifecycle condition did not complete.");
                }
                await Task.Delay(10);
            }
        }

        private static PluginLifecycleCommandOutcome.Succeeded Succeeded(
            PluginLifecycleCommandOutcome outcome,
            string operation
        ) =>
            outcome switch
            {
                PluginLifecycleCommandOutcome.Succeeded succeeded => succeeded,
                PluginLifecycleCommandOutcome.Failed failed => throw new InvalidOperationException(
                    $"Reference {operation} failed in {failed.View.Phase} with {failed.View.LatestOutcome.FailureCode}: {failed.View.LatestOutcome.Detail?.Value ?? "no safe detail"}."
                ),
                PluginLifecycleCommandOutcome.Rejected rejected =>
                    throw new InvalidOperationException(
                        $"Reference {operation} was rejected with {rejected.Code}."
                    ),
                _ => throw new InvalidOperationException(
                    $"Reference {operation} returned an unexpected lifecycle outcome."
                ),
            };

        private static string EncodeTag(string tag)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(tag));
            return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static void AssertRepositoryLayout(string packagePath, PluginManifest manifest)
        {
            Path.GetFileName(packagePath).ShouldBe(manifest.Id.Value);
            Path.GetFileName(Path.GetDirectoryName(packagePath)).ShouldBe("plugins");
        }

        private static async Task<(PluginHostId First, PluginHostId Second)> SeedDatabaseAsync(
            IDbContextFactory<BlokeBotDbContext> database,
            string packagePath,
            PluginManifest manifest
        )
        {
            await using var db = await database.CreateDbContextAsync();
            _ = await db.Database.EnsureCreatedAsync();
            var first = new BotHost
            {
                TwitchUserId = "reference-host-1",
                Login = "reference1",
                DisplayName = "Reference 1",
                EnabledFeatures = HostFeatureFlags.Automations,
                CreatedAtUtc = DateTime.UtcNow,
            };
            var second = new BotHost
            {
                TwitchUserId = "reference-host-2",
                Login = "reference2",
                DisplayName = "Reference 2",
                EnabledFeatures = HostFeatureFlags.Automations,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Hosts.AddRange(first, second);
            _ = await db.SaveChangesAsync();
            var repository = new PluginMarketplaceRepositorySnapshot([
                new(
                    "plugins",
                    PluginMarketplaceRepositoryEntryKind.Directory,
                    ReadOnlyMemory<byte>.Empty
                ),
                new(
                    $"plugins/{manifest.Id.Value}",
                    PluginMarketplaceRepositoryEntryKind.Directory,
                    ReadOnlyMemory<byte>.Empty
                ),
                new(
                    $"plugins/{manifest.Id.Value}/{PluginPackage.ManifestPath}",
                    PluginMarketplaceRepositoryEntryKind.File,
                    await File.ReadAllBytesAsync(
                        Path.Combine(packagePath, PluginPackage.ManifestPath)
                    )
                ),
            ]);
            var entry = PluginMarketplaceRepositoryDiscovery
                .Validate(repository)
                .ShouldBeOfType<PluginMarketplaceRepositoryDiscoveryOutcome.Accepted>()
                .Entries.ShouldHaveSingleItem();
            entry.PluginId.ShouldBe(manifest.Id);
            _ = await new EfPluginMarketplaceCatalogStore(database).ReplaceAsync(
                new(1, DateTimeOffset.UtcNow, [entry]),
                DateTimeOffset.UtcNow,
                null,
                null,
                CancellationToken.None
            );
            PluginHostId.TryCreate(first.Id, out var firstId).ShouldBeTrue();
            PluginHostId.TryCreate(second.Id, out var secondId).ShouldBeTrue();
            return (firstId, secondId);
        }

        private static PluginSettingValues Values(params PluginSettingValueEntry[] entries) =>
            PluginSettingValues.Create(entries) is PluginSettingValuesOutcome.Created created
                ? created.Values
                : throw new InvalidOperationException("Reference settings fixture is invalid.");

        private static PluginSettingValueEntry Entry(string id, PluginSettingValue value) =>
            new(Setting(id), value);

        private static PluginSettingId Setting(string value) =>
            PluginSettingId.TryCreate(value, out var setting)
                ? setting
                : throw new InvalidOperationException("Reference setting ID is invalid.");

        private static PluginFeatureId Feature(string value) =>
            PluginFeatureId.TryCreate(value, out var feature)
                ? feature
                : throw new InvalidOperationException("Reference feature ID is invalid.");

        private static PluginLuaModuleId Module(string value) =>
            PluginLuaModuleId.TryCreate(value, out var module)
                ? module
                : throw new InvalidOperationException("Reference module ID is invalid.");

        private static PluginHostOperationId Operation(string value) =>
            PluginHostOperationId.TryCreate(value, out var operation)
                ? operation
                : throw new InvalidOperationException("Reference operation ID is invalid.");

        private static PluginAutomationDefinitionId Definition(string value) =>
            PluginAutomationDefinitionId.TryCreate(value, out var definition)
                ? definition
                : throw new InvalidOperationException("Reference definition ID is invalid.");

        private static PluginAutomationInvocationId AutomationInvocationId() =>
            PluginAutomationInvocationId.TryCreate(Guid.NewGuid(), out var invocation)
                ? invocation
                : throw new InvalidOperationException(
                    "Reference automation invocation is invalid."
                );

        private static PluginPageId Page(string value) =>
            PluginPageId.TryCreate(value, out var page)
                ? page
                : throw new InvalidOperationException("Reference page ID is invalid.");

        private static PluginPageSessionId PageSessionId() =>
            PluginPageSessionId.TryCreate(Guid.NewGuid(), out var session)
                ? session
                : throw new InvalidOperationException("Reference page session is invalid.");

        private static PluginScheduleHandlerId ScheduleHandler(string value) =>
            PluginScheduleHandlerId.TryCreate(value, out var handler)
                ? handler
                : throw new InvalidOperationException("Reference schedule handler is invalid.");
    }

    private sealed class DelegatingHostCalls : IPluginHostCallDispatcher
    {
        internal IPluginHostCallDispatcher? Target { get; set; }

        public ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PluginHostCallOutcome>(Unavailable());

        public ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginWorkerInvocationIdentity identity,
            PluginHostCall call,
            CancellationToken cancellationToken
        ) =>
            Target is null
                ? ValueTask.FromResult<PluginHostCallOutcome>(Unavailable())
                : Target.DispatchAsync(identity, call, cancellationToken);

        private static PluginHostCallOutcome.Failed Unavailable() =>
            new(new(PluginHostFailureCode.Unavailable, "Reference host calls are unavailable."));
    }

    private sealed class FixtureHttpModule(string endpoint) : IPluginHostModule
    {
        private readonly string _endpoint = endpoint;

        public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Http;

        internal int Attempts { get; private set; }
        internal bool AllRequestsUsedConfiguredEndpoint { get; private set; } = true;
        internal bool AllRequestsHadProtectedAuthorization { get; private set; } = true;

        public ValueTask<PluginHostCallOutcome> InvokeAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        )
        {
            Attempts++;
            var request = Properties((PluginValue.Map)call.Arguments[0]);
            AllRequestsUsedConfiguredEndpoint &=
                request.GetValueOrDefault("url") is PluginValue.String { Value: var url }
                && url == _endpoint;
            var headers = request.GetValueOrDefault("headers") as PluginValue.Map;
            var authorization = headers is null
                ? null
                : Properties(headers).GetValueOrDefault("authorization") as PluginValue.String;
            AllRequestsHadProtectedAuthorization &=
                authorization is { Value.Length: > 7 }
                && authorization.Value.StartsWith("Bearer ", StringComparison.Ordinal);
            return ValueTask.FromResult<PluginHostCallOutcome>(
                new PluginHostCallOutcome.Failed(
                    new(
                        PluginHostFailureCode.Unavailable,
                        "Reference metadata enrichment is unavailable."
                    )
                )
            );
        }
    }

    private sealed class FixtureSchedulesModule : IPluginHostModule
    {
        public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Schedules;

        internal int RecurringCalls { get; private set; }
        internal double LastIntervalSeconds { get; private set; }

        public ValueTask<PluginHostCallOutcome> InvokeAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        )
        {
            if (call.Operation == Descriptor.Operations[1].Id)
            {
                RecurringCalls++;
                LastIntervalSeconds = ((PluginValue.Number)call.Arguments[2]).Value;
                return ValueTask.FromResult<PluginHostCallOutcome>(
                    new PluginHostCallOutcome.Returned(
                        new PluginValue.String(Guid.NewGuid().ToString("D"))
                    )
                );
            }
            return ValueTask.FromResult<PluginHostCallOutcome>(
                new PluginHostCallOutcome.Returned(new PluginValue.Nil())
            );
        }
    }

    private sealed class ReferenceFeatureReconciler : IPluginFeatureReconciler
    {
        private bool _publishingPending = true;

        public ValueTask<PluginFeatureReconciliationResult> ReconcileAsync(
            PluginFeatureReconciliationRequest request,
            CancellationToken cancellationToken
        )
        {
            if (request.Key.FeatureId.Value == "publishing" && _publishingPending)
            {
                _publishingPending = false;
                return ValueTask.FromResult<PluginFeatureReconciliationResult>(
                    new PluginFeatureReconciliationResult.Pending()
                );
            }
            return ValueTask.FromResult<PluginFeatureReconciliationResult>(
                new PluginFeatureReconciliationResult.Ready()
            );
        }

        public ValueTask CancelAsync(
            PluginFeatureKey key,
            PluginLifecycleFence fence,
            PluginFeatureGeneration generation,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    private sealed class AvailableCoreDependencies : IPluginCoreDependencyChecker
    {
        public PluginCoreDependencyStatus Check(
            IReadOnlyList<PluginHostModuleRequirement> requirements
        ) => new PluginCoreDependencyStatus.Available();
    }

    private sealed class ControlledMigrationOwner : IPluginMigrationDataOwner
    {
        internal bool FailNext { get; set; }

        public ValueTask<PluginLifecycleOwnerOutcome> MigrateAsync(
            PluginMigrationContext context,
            CancellationToken cancellationToken
        )
        {
            if (!FailNext)
            {
                return ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
                    new PluginLifecycleOwnerOutcome.Succeeded()
                );
            }
            FailNext = false;
            _ = PluginLifecycleSafeDetail.TryCreate(
                "Selected same-tag replacement migration failed.",
                out var detail
            );
            return ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
                new PluginLifecycleOwnerOutcome.Failed(
                    PluginLifecycleOwnerFailureCode.Failed,
                    detail
                )
            );
        }
    }

    private sealed class RestartingLifecycleWorkers : IPluginLifecycleWorkerManager
    {
        private int _admittedStarts;

        internal int AdmittedStarts => Volatile.Read(ref _admittedStarts);
        internal FixtureLifecycleWorker? Current { get; private set; }

        public ValueTask<PluginLifecycleWorkerStartOutcome> ValidateAsync(
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        ) => Started(new FixtureLifecycleWorker(PluginWorkerMode.Staging));

        public ValueTask<PluginLifecycleWorkerStartOutcome> StartAdmittedAsync(
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        )
        {
            var worker = new FixtureLifecycleWorker(PluginWorkerMode.Admitted);
            Current = worker;
            _ = Interlocked.Increment(ref _admittedStarts);
            return Started(worker);
        }

        private static ValueTask<PluginLifecycleWorkerStartOutcome> Started(
            FixtureLifecycleWorker worker
        ) =>
            ValueTask.FromResult<PluginLifecycleWorkerStartOutcome>(
                new PluginLifecycleWorkerStartOutcome.Started(worker)
            );
    }

    private sealed class FixtureLifecycleWorker(PluginWorkerMode mode)
        : IPluginLifecycleWorkerSession
    {
        private readonly TaskCompletionSource<PluginWorkerFailure> _termination = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public PluginWorkerMode Mode { get; } = mode;
        public Task<PluginWorkerFailure> Termination => _termination.Task;
        internal bool Disposed { get; private set; }

        internal void Terminate(PluginWorkerFailure failure) =>
            _ = _termination.TrySetResult(failure);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
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

    private static IReadOnlyDictionary<string, PluginValue> Properties(PluginValue.Map map) =>
        map.Properties.ToDictionary(
            static property => property.Name,
            static property => property.Value
        );
}
