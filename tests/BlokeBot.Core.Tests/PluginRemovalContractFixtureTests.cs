using System.Collections.Immutable;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginRemovalContractFixtureTests
{
    [Test]
    public async Task DestructiveRemove_UsesAuthoritativeOwnersForDbPackageAutomationAndReceiptState()
    {
        await using var fixture = await AuthoritativeRemovalFixture.CreateAsync();

        var outcome = await fixture.RemoveAsync(packageOwnerFails: false);

        _ = outcome.ShouldBeOfType<PluginLifecycleCommandOutcome.Removed>();
        var observed = await fixture.ObserveAsync();
        observed.PluginLifecyclePresent.ShouldBeFalse();
        observed.PluginDatabaseRows.ShouldBe(0);
        observed.AutomationRows.ShouldBe(0);
        observed.PackagePresent.ShouldBeFalse();
        observed.ReceiptPresent.ShouldBeFalse();
        observed.GlobalCatalogueMetadataPresent.ShouldBeTrue();
    }

    [Test]
    public async Task DestructiveRemove_RetainedPackageFaultsCanonicalLifecycleAndLeavesEvidence()
    {
        await using var fixture = await AuthoritativeRemovalFixture.CreateAsync();

        var outcome = await fixture.RemoveAsync(packageOwnerFails: true);

        var failure = outcome.ShouldBeOfType<PluginLifecycleCommandOutcome.Failed>().View;
        failure.Phase.ShouldBe(PluginLifecyclePhase.Faulted);
        failure.LatestOutcome.FailureCode.ShouldBe(PluginLifecycleFailureCode.RemovalFailed);
        var observed = await fixture.ObserveAsync();
        observed.PackagePresent.ShouldBeTrue();
        observed.ReceiptPresent.ShouldBeTrue();
        observed.PluginLifecyclePresent.ShouldBeTrue();
    }

    private sealed class AuthoritativeRemovalFixture : IAsyncDisposable
    {
        private readonly SqliteBlokeBotDbFactory _database;
        private readonly string _root;
        private readonly PluginId _pluginId;
        private readonly PluginLifecyclePackage _package;
        private readonly PluginMarketplacePackageStore _packages;
        private readonly EfPluginMarketplaceReceiptStore _receipts;
        private readonly PluginFeatureRemovalOwner _features;
        private readonly EfPluginLifecycleStore _lifecycles;
        private readonly PluginRuntimeSnapshotRegistry _runtime;
        private readonly FixtureLifecycleWorkers _workers;
        private readonly PluginLifecycleCommandOutcome.Succeeded _active;

        private AuthoritativeRemovalFixture(
            SqliteBlokeBotDbFactory database,
            string root,
            PluginId pluginId,
            PluginLifecyclePackage package,
            PluginMarketplacePackageStore packages,
            EfPluginMarketplaceReceiptStore receipts,
            PluginFeatureRemovalOwner features,
            EfPluginLifecycleStore lifecycles,
            PluginRuntimeSnapshotRegistry runtime,
            FixtureLifecycleWorkers workers,
            PluginLifecycleCommandOutcome.Succeeded active
        )
        {
            _database = database;
            _root = root;
            _pluginId = pluginId;
            _package = package;
            _packages = packages;
            _receipts = receipts;
            _features = features;
            _lifecycles = lifecycles;
            _runtime = runtime;
            _workers = workers;
            _active = active;
        }

        internal static async Task<AuthoritativeRemovalFixture> CreateAsync()
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var root = Path.Combine(
                Path.GetTempPath(),
                $"blokebot-removal-contract-{Guid.NewGuid():N}"
            );
            _ = Directory.CreateDirectory(root);
            var pluginId = PluginContractFixtures.PluginId("community.link-queue");
            await SeedStateAsync(database, pluginId);
            var package = Package(root, pluginId);
            var packages = new PluginMarketplacePackageStore(
                new(
                    Path.Combine(root, "packages"),
                    Path.Combine(root, "private"),
                    TimeSpan.FromHours(1)
                ),
                new UnavailableArchiveTransport(),
                new(),
                new(
                    PluginContractFixtures.CompatibleHost(),
                    new UnavailableHostCalls(),
                    NullLogger<PluginWorkerClient>.Instance
                )
            );
            var receipts = new EfPluginMarketplaceReceiptStore(database);
            await receipts.SaveAsync(
                new(
                    pluginId,
                    PluginMarketplaceOperationKind.Install,
                    package.Installation.Release,
                    "Activated",
                    null,
                    DateTimeOffset.UtcNow
                ),
                CancellationToken.None
            );
            var runtime = new PluginRuntimeSnapshotRegistry();
            var workers = new FixtureLifecycleWorkers();
            var features = new PluginFeatureRemovalOwner(
                new EfPluginFeatureStore(database, new()),
                new()
            );
            var lifecycles = new EfPluginLifecycleStore(database);
            var coordinator = Coordinator(
                lifecycles,
                runtime,
                workers,
                [features, packages, receipts]
            );
            var active = (
                await coordinator.ActivateAsync(
                    PluginLifecycleOperationId.New(),
                    package,
                    CancellationToken.None
                )
            ).ShouldBeOfType<PluginLifecycleCommandOutcome.Succeeded>();
            return new(
                database,
                root,
                pluginId,
                package,
                packages,
                receipts,
                features,
                lifecycles,
                runtime,
                workers,
                active
            );
        }

        internal async ValueTask<PluginLifecycleCommandOutcome> RemoveAsync(bool packageOwnerFails)
        {
            IPluginRemovalDataOwner packageOwner = packageOwnerFails
                ? new FailingPackageRemovalOwner()
                : _packages;
            var coordinator = Coordinator(
                _lifecycles,
                _runtime,
                _workers,
                [_features, packageOwner, _receipts]
            );
            return await coordinator.RemoveAsync(
                _pluginId,
                PluginLifecycleOperationId.New(),
                CancellationToken.None
            );
        }

        internal async Task<ObservedRemovalState> ObserveAsync()
        {
            await using var database = await _database.CreateDbContextAsync();
            var pluginRows =
                await database.PluginInstallationConfigurations.CountAsync()
                + await database.PluginInstallationSecrets.CountAsync()
                + await database.PluginFeatureConfigurations.CountAsync()
                + await database.PluginFeatureSecrets.CountAsync()
                + await database.PluginFeatureStates.CountAsync()
                + await database.PluginAutomationInstantiations.CountAsync();
            var automationRows =
                await database.AutomationFlows.CountAsync()
                + await database.AutomationFlowNodes.CountAsync()
                + await database.AutomationFlowRuns.CountAsync();
            return new(
                await database.PluginLifecycles.AnyAsync(value =>
                    value.PluginId == _pluginId.Value
                ),
                pluginRows,
                automationRows,
                Directory.Exists(Path.GetDirectoryName(_package.PreparedPackage.PackageRoot)!),
                await database.PluginMarketplaceReceipts.AnyAsync(value =>
                    value.PluginId == _pluginId.Value
                ),
                await database.PluginMarketplaceCatalogEntries.AnyAsync(value =>
                    value.PluginId == _pluginId.Value
                )
            );
        }

        public async ValueTask DisposeAsync()
        {
            await _database.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static PluginLifecycleCoordinator Coordinator(
            IPluginLifecycleStore lifecycles,
            PluginRuntimeSnapshotRegistry runtime,
            FixtureLifecycleWorkers workers,
            IReadOnlyList<IPluginRemovalDataOwner> removalOwners
        ) =>
            new(
                lifecycles,
                new UnavailablePackageResolver(),
                [],
                [],
                removalOwners,
                new FixturePendingWorkCanceller(),
                workers,
                runtime,
                new(),
                new(TimeSpan.FromSeconds(2), TimeSpan.Zero),
                TimeProvider.System,
                NullLogger<PluginLifecycleCoordinator>.Instance
            );

        private static PluginLifecyclePackage Package(string root, PluginId pluginId)
        {
            var accepted = PluginManifestJson
                .Validate(
                    PluginContractFixtures.CompleteManifestJson(),
                    PluginContractFixtures.CompatibleHost()
                )
                .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
                .Manifest;
            var manifest = accepted.Manifest;
            var packageRoot = Path.Combine(root, "packages", pluginId.Value, "selected", "package");
            _ = Directory.CreateDirectory(packageRoot);
            File.WriteAllText(Path.Combine(packageRoot, "installed.marker"), "installed");
            var prepared = new PreparedPluginWorkerPackage(
                new(
                    new(pluginId, manifest.Release),
                    PluginRuntimeIdentifier.LinuxX64,
                    manifest.EntryModule,
                    manifest
                        .LuaModules.Select(module => new PluginWorkerLuaModule(
                            module.Id,
                            module.Path
                        ))
                        .ToImmutableArray()
                ),
                packageRoot
            )
            {
                Manifest = accepted,
            };
            return new(
                prepared.Descriptor.Plugin,
                PluginPackageOperationId.New(),
                prepared,
                Path.Combine(root, "private", pluginId.Value),
                new UnavailableHostCalls(),
                NullLogger<PluginWorkerClient>.Instance
            );
        }

        private static async Task SeedStateAsync(
            SqliteBlokeBotDbFactory database,
            PluginId pluginId
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
            var flowId = Guid.NewGuid();
            var nodeId = Guid.NewGuid();
            _ = context.PluginInstallationConfigurations.Add(
                new()
                {
                    PluginId = pluginId.Value,
                    ValuesJson = "[]",
                    Revision = 0,
                }
            );
            _ = context.PluginInstallationSecrets.Add(
                new()
                {
                    PluginId = pluginId.Value,
                    SettingId = "token",
                    ProtectedValue = [1],
                }
            );
            _ = context.PluginFeatureConfigurations.Add(
                new()
                {
                    PluginId = pluginId.Value,
                    FeatureId = "collection",
                    HostId = host.Id,
                    ValuesJson = "[]",
                    Revision = 0,
                }
            );
            _ = context.PluginFeatureSecrets.Add(
                new()
                {
                    PluginId = pluginId.Value,
                    FeatureId = "collection",
                    HostId = host.Id,
                    SettingId = "secret",
                    ProtectedValue = [2],
                }
            );
            _ = context.PluginFeatureStates.Add(
                new()
                {
                    PluginId = pluginId.Value,
                    FeatureId = "collection",
                    HostId = host.Id,
                    LifecycleOperationId = Guid.NewGuid(),
                    WorkerGeneration = 1,
                    FeatureGeneration = 1,
                    Readiness = PluginFeatureReadinessKind.Ready,
                    Revision = 1,
                }
            );
            var flow = new AutomationFlow
            {
                Id = flowId,
                HostId = host.Id,
                Name = "Plugin-owned flow",
                SchemaVersion = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Nodes =
                [
                    new()
                    {
                        Id = nodeId,
                        FlowId = flowId,
                        DefinitionId = $"plugin.{pluginId.Value}.action",
                        DefinitionSchemaVersion = 1,
                        ConfigurationJson = "{}",
                        InputBindingsJson = "{}",
                        PluginProvenanceJson = $"{{\"pluginId\":\"{pluginId.Value}\"}}",
                    },
                ],
            };
            _ = context.AutomationFlows.Add(flow);
            _ = context.PluginAutomationInstantiations.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    EnableOperationId = Guid.NewGuid(),
                    PluginId = pluginId.Value,
                    FeatureId = "collection",
                    HostId = host.Id,
                    TemplateId = "fixture",
                    PluginVersion = "1.2.0",
                    MutableTag = "community-link-queue",
                    ManifestVersion = 1,
                    TemplateHash = "fixture",
                    Status = PluginAutomationInstantiationStatus.Completed,
                    FlowId = flowId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
            _ = context.AutomationFlowRuns.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    FlowId = flowId,
                    HostId = host.Id,
                    AutomationGeneration = 1,
                    RequiredFeatures = HostFeatureFlags.Automations,
                    ContextSchemaVersion = 1,
                    SourceDefinitionId = flow.Nodes[0].DefinitionId,
                    SourceNodeId = nodeId,
                    SourceOccurrenceId = Guid.NewGuid(),
                    ContextJson = "{}",
                    DefinitionJson = "{}",
                    Status = AutomationFlowRunStatus.Completed,
                    StartedAtUtc = now,
                    CompletedAtUtc = now,
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
                    PluginId = pluginId.Value,
                    DeclaredVersion = "1.2.0",
                    MutableTag = "community-link-queue",
                    Name = "Link queue",
                    Summary = "Fixture",
                    Author = "Community",
                    RepositoryUrl = "https://github.com/community/plugins",
                    PackagePath = "plugins/link-queue",
                    CompatibilityBlokeBot = ">=0.13.0 <0.14.0",
                    CompatibilityPluginApi = "1",
                    CompatibilityLua = "5.4",
                }
            );
            _ = await context.SaveChangesAsync();
        }
    }

    private sealed record ObservedRemovalState(
        bool PluginLifecyclePresent,
        int PluginDatabaseRows,
        int AutomationRows,
        bool PackagePresent,
        bool ReceiptPresent,
        bool GlobalCatalogueMetadataPresent
    );

    private sealed class FailingPackageRemovalOwner : IPluginRemovalDataOwner
    {
        public ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
            PluginRemovalContext context,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
                new PluginLifecycleOwnerOutcome.Failed(PluginLifecycleOwnerFailureCode.Failed, null)
            );
    }

    private sealed class FixturePendingWorkCanceller : IPluginPendingWorkCanceller
    {
        public ValueTask<PluginLifecycleOwnerOutcome> CancelAsync(
            PluginId pluginId,
            PluginLifecycleFence fence,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
                new PluginLifecycleOwnerOutcome.Succeeded()
            );
    }

    private sealed class FixtureLifecycleWorkers : IPluginLifecycleWorkerManager
    {
        public ValueTask<PluginLifecycleWorkerStartOutcome> ValidateAsync(
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        ) => Started(package, PluginWorkerMode.Staging);

        public ValueTask<PluginLifecycleWorkerStartOutcome> StartAdmittedAsync(
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        ) => Started(package, PluginWorkerMode.Admitted);

        private static ValueTask<PluginLifecycleWorkerStartOutcome> Started(
            PluginLifecyclePackage package,
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

    private sealed class UnavailablePackageResolver : IPluginLifecyclePackageResolver
    {
        public ValueTask<PluginLifecyclePackageResolution> ResolveAsync(
            PluginInstallationIdentity installation,
            PluginPackageOperationId packageOperationId,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginLifecyclePackageResolution>(
                new PluginLifecyclePackageResolution.Unavailable()
            );
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
