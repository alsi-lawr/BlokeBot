using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Persistence;
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

public sealed class PluginMarketplaceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void CatalogValidation_RequiresStrictV1CuratedMutableTagMetadata()
    {
        var accepted = PluginMarketplaceCatalogParser
            .Validate(Catalog())
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Accepted>();

        var entry = accepted.Entries.ShouldHaveSingleItem();
        entry.PluginId.Value.ShouldBe("community.link-queue");
        entry.Release.DeclaredVersion.Value.ShouldBe("1.2.0");
        entry.Release.Tag.Value.ShouldBe("community-link-queue");
        entry.MediaUrls.ShouldHaveSingleItem().Host.ShouldBe("cdn.example.test");

        _ = PluginMarketplaceCatalogParser
            .Validate(Catalog(tag: "36697a9e3b436713d939f30d9007febb1e3e1eda"))
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Rejected>();
        _ = PluginMarketplaceCatalogParser
            .Validate(Catalog(repository: "https://github.com/someone/other"))
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Accepted>();
        _ = PluginMarketplaceCatalogParser
            .Validate(Catalog(repository: "https://gitlab.com/someone/other"))
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Rejected>();
        _ = PluginMarketplaceCatalogParser
            .Validate(Catalog(extra: "\"unexpected\":true,"))
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Rejected>();
        _ = PluginMarketplaceCatalogParser
            .Validate(Encoding.UTF8.GetBytes("{partial"))
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Rejected>();
        _ = PluginMarketplaceCatalogParser
            .Validate(Catalog(schemaVersion: 2))
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Rejected>();
        _ = PluginMarketplaceCatalogParser
            .Validate(Catalog(blokeBot: "not-a-range"))
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Rejected>();
        var padded = Catalog().Concat(new byte[2 * 1024 * 1024]).ToArray();
        padded.AsSpan(Catalog().Length).Fill((byte)' ');
        _ = PluginMarketplaceCatalogParser
            .Validate(padded)
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Accepted>();
    }

    [Test]
    public async Task GitHubTransports_UseOnlyFixedRawCatalogAndSelectedMutableTagArchiveUrls()
    {
        var handler = new RecordingHttpHandler(request =>
            request.RequestUri == GitHubPluginMarketplaceCatalogTransport.CatalogUrl
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Catalog()),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3]),
                }
        );
        var clients = new FixedHttpClientFactory(handler);
        var catalog = new GitHubPluginMarketplaceCatalogTransport(clients);
        _ = (
            await catalog.DownloadAsync("\"previous\"", _now, CancellationToken.None)
        ).ShouldBeOfType<PluginMarketplaceCatalogDownload.Delivered>();
        _ = PluginGitTag.TryCreate("release/v1.2.0", out var tag);
        var archives = new GitHubPluginMarketplaceArchiveTransport(clients);
        using var root = new TemporaryDirectory();
        var archivePath = Path.Combine(root.Path, "archive.tar.gz");
        _ = (
            await archives.DownloadAsync(
                new Uri("https://github.com/community/blokebot-plugins"),
                tag,
                archivePath,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginMarketplaceArchiveDownload.Delivered>();
        (await File.ReadAllBytesAsync(archivePath)).ShouldBe([1, 2, 3]);

        handler.Requests.Count.ShouldBe(2);
        handler.ETags[0].ShouldBe("\"previous\"");
        handler.ModifiedSince[0].ShouldBe(_now);
        handler
            .Requests[0]
            .ShouldBe(
                "https://raw.githubusercontent.com/alsi-lawr/blokebot-plugins/master/catalog.json"
            );
        handler
            .Requests[1]
            .ShouldBe(
                "https://codeload.github.com/community/blokebot-plugins/tar.gz/refs/tags/release%2Fv1.2.0"
            );
    }

    [Test]
    public async Task RefreshSnapshot_SearchIsLocalAndRetainsLastValidAcrossOutageAndRestart()
    {
        using var root = new TemporaryDirectory();
        var clock = new ManualTimeProvider(_now);
        var transport = new QueueCatalogTransport(
            new PluginMarketplaceCatalogDownload.Delivered(Catalog(), null, null),
            new PluginMarketplaceCatalogDownload.Failed()
        );
        var database = await CreateDatabaseAsync(root.Path);
        using var registry = new PluginMarketplaceCatalogRegistry(
            new EfPluginMarketplaceCatalogStore(database),
            transport,
            clock
        );
        await registry.InitializeAsync(CancellationToken.None);
        await registry.RefreshAsync(CancellationToken.None);
        var service = CreateCatalogService(registry, clock);

        var available = service
            .Search(Admin(), "queue")
            .ShouldBeOfType<PluginMarketplaceSearchOutcome.Available>();
        available.Entries.ShouldHaveSingleItem().Name.ShouldBe("Link queue");
        transport.Calls.ShouldBe(1);

        clock.Advance(TimeSpan.FromHours(2));
        await registry.RefreshAsync(CancellationToken.None);
        var stale = service
            .Search(Admin(), null)
            .ShouldBeOfType<PluginMarketplaceSearchOutcome.Available>();
        stale.Age.ShouldBe(TimeSpan.FromHours(2));
        stale.RefreshFailure.ShouldBe(PluginMarketplaceRefreshFailureCode.DownloadFailed);

        var restartTransport = new QueueCatalogTransport();
        using var restarted = new PluginMarketplaceCatalogRegistry(
            new EfPluginMarketplaceCatalogStore(new TestDbContextFactory(database.Options)),
            restartTransport,
            clock
        );
        await restarted.InitializeAsync(CancellationToken.None);
        _ = CreateCatalogService(restarted, clock)
            .Search(Admin(), null)
            .ShouldBeOfType<PluginMarketplaceSearchOutcome.Available>();
        restartTransport.Calls.ShouldBe(0);

        await using var context = database.CreateDbContext();
        (await context.PluginMarketplaceCatalogEntries.CountAsync()).ShouldBe(1);
        (await context.Set<PluginMarketplaceCatalogTagRecord>().CountAsync()).ShouldBe(2);
        var columns = await context
            .Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM pragma_table_info('plugin_marketplace_catalog_entries')"
            )
            .ToArrayAsync();
        columns.ShouldNotContain(value =>
            value.Contains("sha", StringComparison.OrdinalIgnoreCase)
        );
        columns.ShouldNotContain(value =>
            value.Contains("archive", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Test]
    public Task ConditionalRefresh_ETagOnlySurvivesRestartAnd304() =>
        AssertConditionalRestartAsync("\"catalog-v1\"", null);

    [Test]
    public Task ConditionalRefresh_LastModifiedOnlySurvivesRestartAnd304() =>
        AssertConditionalRestartAsync(null, _now.AddMinutes(-5));

    [Test]
    public Task ConditionalRefresh_ETagAndLastModifiedSurviveRestartAnd304() =>
        AssertConditionalRestartAsync("W/\"catalog-v1\"", _now.AddMinutes(-5));

    [Test]
    public async Task CatalogStore_ReplacementIsAtomicWhenTheNewSnapshotFails()
    {
        using var root = new TemporaryDirectory();
        var database = await CreateDatabaseAsync(root.Path);
        var store = new EfPluginMarketplaceCatalogStore(database);
        var entry = Entry();
        var first = new PluginMarketplaceCatalogSnapshot(1, _now, [entry]);
        _ = await store.ReplaceAsync(first, _now, null, null, CancellationToken.None);
        var invalidReplacement = new PluginMarketplaceCatalogSnapshot(
            1,
            _now.AddHours(1),
            [entry with { Name = "Replacement" }, entry]
        );

        _ = await Should.ThrowAsync<InvalidOperationException>(() =>
            store
                .ReplaceAsync(
                    invalidReplacement,
                    _now.AddHours(1),
                    null,
                    null,
                    CancellationToken.None
                )
                .AsTask()
        );

        var retained = await store.LoadAsync(CancellationToken.None);
        retained.LastValid.ShouldNotBeNull().RefreshedAt.ShouldBe(_now);
        retained.LastValid.Entries.ShouldHaveSingleItem().Name.ShouldBe("Link queue");
    }

    [Test]
    public async Task FirstRunOutage_IsUnavailableAndSearchRequiresBotAdmin()
    {
        using var root = new TemporaryDirectory();
        var clock = new ManualTimeProvider(_now);
        var database = await CreateDatabaseAsync(root.Path);
        using var registry = new PluginMarketplaceCatalogRegistry(
            new EfPluginMarketplaceCatalogStore(database),
            new QueueCatalogTransport(new PluginMarketplaceCatalogDownload.Failed()),
            clock
        );
        await registry.InitializeAsync(CancellationToken.None);
        await registry.RefreshAsync(CancellationToken.None);
        var service = CreateCatalogService(registry, clock);

        _ = service
            .Search(Admin() with { IsBotAdmin = false }, null)
            .ShouldBeOfType<PluginMarketplaceSearchOutcome.Unauthorized>();
        var unavailable = service
            .Search(Admin(), null)
            .ShouldBeOfType<PluginMarketplaceSearchOutcome.Unavailable>();
        unavailable.RefreshFailure.ShouldBe(PluginMarketplaceRefreshFailureCode.DownloadFailed);
    }

    [Test]
    public async Task HostedRefresh_RunsImmediatelyThenOnConfiguredCadence()
    {
        using var root = new TemporaryDirectory();
        var options = Options(root.Path);
        var clock = new ManualPeriodicTimeProvider(_now);
        var transport = new QueueCatalogTransport(
            new PluginMarketplaceCatalogDownload.Delivered(Catalog(), null, null),
            new PluginMarketplaceCatalogDownload.Failed()
        );
        var database = await CreateDatabaseAsync(root.Path);
        using var registry = new PluginMarketplaceCatalogRegistry(
            new EfPluginMarketplaceCatalogStore(database),
            transport,
            clock
        );
        var packages = new PluginMarketplacePackageStore(
            options,
            new FixedArchiveTransport(null),
            new(),
            Runtime()
        );
        using var hosted = new PluginMarketplaceRefreshService(
            registry,
            packages,
            options,
            clock,
            NullLogger<PluginMarketplaceRefreshService>.Instance
        );

        await hosted.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => transport.Calls == 1);
        clock.Advance(options.RefreshInterval);
        await WaitUntilAsync(() => transport.Calls == 2);
        await hosted.StopAsync(CancellationToken.None);

        _ = registry.Current.LastValid.ShouldNotBeNull();
        registry.Current.RefreshFailure.ShouldBe(
            PluginMarketplaceRefreshFailureCode.DownloadFailed
        );
    }

    [Test]
    public async Task ArchiveReader_StreamsSelectedPathAndValidatesB244Declarations()
    {
        using var root = new TemporaryDirectory();
        var reader = new PluginMarketplaceArchiveReader();
        var archivePath = Path.Combine(root.Path, "archive.tar.gz");
        var packagePath = Path.Combine(root.Path, "package");
        await File.WriteAllBytesAsync(
            archivePath,
            Archive(PluginContractFixtures.CompletePackage())
        );
        _ = Directory.CreateDirectory(packagePath);

        _ = (
            await reader.ExtractAsync(archivePath, "plugins/link-queue", packagePath, default)
        ).ShouldBeOfType<PluginMarketplaceArchiveReadOutcome.Accepted>();
        var validated = (
            await PluginMarketplaceMaterializedPackageValidator.ValidateAsync(
                packagePath,
                PluginContractFixtures.CompatibleHost(),
                default
            )
        ).ShouldBeOfType<PluginMarketplaceMaterializedPackageValidationOutcome.Accepted>();

        validated
            .Package.Manifest.ShouldNotBeNull()
            .Manifest.Release.DeclaredVersion.Value.ShouldBe("1.2.0");
        (
            await File.ReadAllBytesAsync(
                Path.Combine(packagePath, "payloads/managed/Queue.Helper.dll")
            )
        )[0]
            .ShouldBe((byte)0x4D);
    }

    [Test]
    [Arguments(ArchiveAttack.SymbolicLink)]
    [Arguments(ArchiveAttack.HardLink)]
    [Arguments(ArchiveAttack.CaseCollision)]
    [Arguments(ArchiveAttack.Traversal)]
    [Arguments(ArchiveAttack.AbsolutePath)]
    [Arguments(ArchiveAttack.WindowsAbsolutePath)]
    public async Task ArchiveReader_RejectsUnsafeArchiveShapes(ArchiveAttack attack)
    {
        using var root = new TemporaryDirectory();
        var reader = new PluginMarketplaceArchiveReader();
        var archivePath = Path.Combine(root.Path, "attack.tar.gz");
        var packagePath = Path.Combine(root.Path, "package");
        await File.WriteAllBytesAsync(archivePath, AttackArchive(attack));
        _ = Directory.CreateDirectory(packagePath);

        _ = (
            await reader.ExtractAsync(archivePath, "plugins/link-queue", packagePath, default)
        ).ShouldBeOfType<PluginMarketplaceArchiveReadOutcome.Rejected>();
    }

    [Test]
    public async Task ArchiveReader_DoesNotImposeAWholeRepositoryEntryLimit()
    {
        using var root = new TemporaryDirectory();
        var archive = Archive(PluginContractFixtures.CompletePackage(), unrelatedEntries: 4_100);
        var archivePath = Path.Combine(root.Path, "large-index.tar.gz");
        var packagePath = Path.Combine(root.Path, "package");
        await File.WriteAllBytesAsync(archivePath, archive);
        _ = Directory.CreateDirectory(packagePath);

        _ = (
            await new PluginMarketplaceArchiveReader().ExtractAsync(
                archivePath,
                "plugins/link-queue",
                packagePath,
                default
            )
        ).ShouldBeOfType<PluginMarketplaceArchiveReadOutcome.Accepted>();
    }

    [Test]
    public async Task ArchiveReader_CancellationStopsStreamingExtraction()
    {
        using var root = new TemporaryDirectory();
        var archivePath = Path.Combine(root.Path, "cancel.tar.gz");
        var packagePath = Path.Combine(root.Path, "package");
        await File.WriteAllBytesAsync(
            archivePath,
            Archive(PluginContractFixtures.CompletePackage(), unrelatedEntries: 100)
        );
        _ = Directory.CreateDirectory(packagePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            new PluginMarketplaceArchiveReader()
                .ExtractAsync(archivePath, "plugins/link-queue", packagePath, cancellation.Token)
                .AsTask()
        );
    }

    [Test]
    public async Task PackageStore_DoesNotPoliceReviewedFileByteDeclarations()
    {
        using var root = new TemporaryDirectory();
        var package = PluginContractFixtures.CompletePackage().ToArray();
        var module = package
            .OfType<PluginPackageEntry.File>()
            .Single(entry => entry.Path == "lua/main.lua");
        package[Array.IndexOf(package, module)] = new PluginPackageEntry.File(
            module.Path,
            new byte[PluginContractLimits.MaximumLuaModuleBytes + 1]
        );
        var store = new PluginMarketplacePackageStore(
            Options(root.Path),
            new FixedArchiveTransport(Archive(package)),
            new(),
            Runtime()
        );

        _ = (
            await store.PrepareAsync(
                Entry(),
                PluginPackageOperationId.New(),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginMarketplacePackagePreparationOutcome.Prepared>();
    }

    [Test]
    public async Task PackageStore_RejectsCatalogAndB244ManifestIncompatibility()
    {
        using var root = new TemporaryDirectory();
        var archive = new FixedArchiveTransport(Archive(PluginContractFixtures.CompletePackage()));
        var store = new PluginMarketplacePackageStore(
            Options(root.Path),
            archive,
            new(),
            Runtime()
        );
        var incompatibleEntry = Entry() with
        {
            Compatibility = Entry().Compatibility with { Targets = ["win-x64"] },
        };

        _ = (
            await store.PrepareAsync(
                incompatibleEntry,
                PluginPackageOperationId.New(),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginMarketplacePackagePreparationOutcome.Rejected>();
        archive.Calls.ShouldBe(0);

        var incompatibleManifest = PluginContractFixtures
            .CompletePackage()
            .Select(entry =>
                entry is PluginPackageEntry.File { Path: PluginPackage.ManifestPath }
                    ? new PluginPackageEntry.File(
                        PluginPackage.ManifestPath,
                        Encoding.UTF8.GetBytes(
                            Encoding
                                .UTF8.GetString(PluginContractFixtures.CompleteManifestJson())
                                .Replace(
                                    "\"minimumBlokeBotVersion\": \"0.13.0\"",
                                    "\"minimumBlokeBotVersion\": \"0.14.0\"",
                                    StringComparison.Ordinal
                                )
                                .Replace(
                                    "\"maximumBlokeBotVersionExclusive\": \"0.14.0\"",
                                    "\"maximumBlokeBotVersionExclusive\": \"0.15.0\"",
                                    StringComparison.Ordinal
                                )
                        )
                    )
                    : entry
            )
            .ToArray();
        var manifestStore = new PluginMarketplacePackageStore(
            Options(Path.Combine(root.Path, "manifest")),
            new FixedArchiveTransport(Archive(incompatibleManifest)),
            new(),
            Runtime()
        );
        _ = (
            await manifestStore.PrepareAsync(
                Entry(),
                PluginPackageOperationId.New(),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginMarketplacePackagePreparationOutcome.Rejected>();
    }

    [Test]
    public async Task PackageStore_IsDurableIdentityResolverAndRemovalOwner()
    {
        using var root = new TemporaryDirectory();
        var options = Options(root.Path);
        var packageStore = new PluginMarketplacePackageStore(
            options,
            new FixedArchiveTransport(Archive(PluginContractFixtures.CompletePackage())),
            new(),
            Runtime()
        );
        var entry = Entry();
        var packageOperationId = PluginPackageOperationId.New();

        var prepared = (
            await packageStore.PrepareAsync(entry, packageOperationId, CancellationToken.None)
        ).ShouldBeOfType<PluginMarketplacePackagePreparationOutcome.Prepared>();
        prepared.Package.MatchesIdentity.ShouldBeTrue();
        prepared.Package.StateRoot.ShouldStartWith(options.PluginPrivateStateRoot);
        prepared.Package.PreparedPackage.PackageRoot.ShouldStartWith(options.PackageStateRoot);
        prepared.Package.PreparedPackage.PackageRoot.ShouldNotContain("private.db");

        var restarted = new PluginMarketplacePackageStore(
            options,
            new FixedArchiveTransport(null),
            new(),
            Runtime()
        );
        _ = (
            await restarted.ResolveAsync(
                prepared.Package.Installation,
                packageOperationId,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecyclePackageResolution.Available>();
        _ = (
            await restarted.ResolveAsync(
                prepared.Package.Installation,
                PluginPackageOperationId.New(),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecyclePackageResolution.Unavailable>();

        var removal = await restarted.RemoveAsync(
            new PluginRemovalContext(entry.PluginId, FixtureFence()),
            CancellationToken.None
        );
        _ = removal.ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();
        _ = (
            await restarted.ResolveAsync(
                prepared.Package.Installation,
                packageOperationId,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecyclePackageResolution.Unavailable>();
    }

    [Test]
    public async Task PackageStore_CorruptExactPackageNeverFallsBackToOlderSameTagPackage()
    {
        using var root = new TemporaryDirectory();
        var store = new PluginMarketplacePackageStore(
            Options(root.Path),
            new QueueArchiveTransport(
                Archive(PluginContractFixtures.CompletePackage()),
                Archive(PluginContractFixtures.CompletePackage())
            ),
            new(),
            Runtime()
        );
        var older = (
            await store.PrepareAsync(
                Entry(),
                PluginPackageOperationId.New(),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginMarketplacePackagePreparationOutcome.Prepared>()
            .Package;
        var selected = (
            await store.PrepareAsync(
                Entry(),
                PluginPackageOperationId.New(),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginMarketplacePackagePreparationOutcome.Prepared>()
            .Package;
        await File.WriteAllTextAsync(
            Path.Combine(selected.PreparedPackage.PackageRoot, PluginPackage.ManifestPath),
            "{}"
        );

        _ = (
            await store.ResolveAsync(
                selected.Installation,
                selected.PackageOperationId,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecyclePackageResolution.Unavailable>();
        _ = (
            await store.ResolveAsync(
                older.Installation,
                older.PackageOperationId,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecyclePackageResolution.Available>();
    }

    [Test]
    public async Task PackageStore_BackfillsLegacyPackageToPersistedExactOperation()
    {
        using var root = new TemporaryDirectory();
        var store = new PluginMarketplacePackageStore(
            Options(root.Path),
            new FixedArchiveTransport(Archive(PluginContractFixtures.CompletePackage())),
            new(),
            Runtime()
        );
        var package = (
            await store.PrepareAsync(
                Entry(),
                PluginPackageOperationId.New(),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PluginMarketplacePackagePreparationOutcome.Prepared>()
            .Package;
        var operationDirectory = Directory.GetParent(package.PreparedPackage.PackageRoot)!;
        var tagDirectory = operationDirectory.Parent!.Parent!;
        var legacyRoot = Path.Combine(tagDirectory.FullName, "package");
        Directory.Move(package.PreparedPackage.PackageRoot, legacyRoot);
        _ = PluginWorkerGeneration.TryCreate(1, out var generation);
        var lifecycleOperationId = PluginLifecycleOperationId.New();
        var now = DateTimeOffset.UtcNow;
        var state = new PluginLifecycleState(
            package.Installation.PluginId,
            package.Installation,
            package.PackageOperationId,
            lifecycleOperationId,
            generation,
            new(
                package.Installation,
                new(lifecycleOperationId, generation),
                package.PackageOperationId
            ),
            PluginLifecyclePhase.Active,
            PluginLifecycleOperationKind.Activate,
            null,
            false,
            null,
            PluginLifecycleOutcome.Progress(PluginLifecycleOutcomeCode.Activated, now),
            1,
            now
        );

        await store.BackfillLegacyPackagesAsync([state], CancellationToken.None);

        _ = (
            await store.ResolveAsync(
                package.Installation,
                package.PackageOperationId,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginLifecyclePackageResolution.Available>();
        Directory.Exists(legacyRoot).ShouldBeFalse();
    }

    [Test]
    public async Task PackageStore_CleansOnlyInterruptedStagingDirectories()
    {
        using var root = new TemporaryDirectory();
        var options = Options(root.Path);
        var interrupted = Path.Combine(
            options.PackageStateRoot,
            "plugin",
            "1.0.0",
            "tag",
            "package.preparing-fixture"
        );
        var retained = Path.Combine(options.PackageStateRoot, "plugin", "retained");
        var interruptedArchive = Path.Combine(
            options.PackageStateRoot,
            "plugin",
            "archive.preparing-fixture.tar.gz"
        );
        _ = Directory.CreateDirectory(interrupted);
        _ = Directory.CreateDirectory(retained);
        await File.WriteAllBytesAsync(interruptedArchive, [1]);
        var store = new PluginMarketplacePackageStore(
            options,
            new FixedArchiveTransport(null),
            new(),
            Runtime()
        );

        await store.CleanupInterruptedAsync(CancellationToken.None);

        Directory.Exists(interrupted).ShouldBeFalse();
        File.Exists(interruptedArchive).ShouldBeFalse();
        Directory.Exists(retained).ShouldBeTrue();
    }

    [Test]
    public async Task ProductionActivationPublisher_ProjectsPagesAndAutomationCatalogByExactFence()
    {
        using var root = new TemporaryDirectory();
        var packageOperationId = PluginPackageOperationId.New();
        var operationId = PluginLifecycleOperationId.New();
        var packages = new PluginMarketplacePackageStore(
            Options(root.Path),
            new FixedArchiveTransport(Archive(PluginContractFixtures.CompletePackage())),
            new(),
            Runtime()
        );
        var package = (
            await packages.PrepareAsync(Entry(), packageOperationId, CancellationToken.None)
        )
            .ShouldBeOfType<PluginMarketplacePackagePreparationOutcome.Prepared>()
            .Package;
        _ = PluginWorkerGeneration.TryCreate(1, out var workerGeneration);
        var fence = new PluginLifecycleFence(operationId, workerGeneration);
        var runtime = new PluginRuntimeSnapshotRegistry();
        var dispatch = new PluginDispatchSnapshotRegistry(runtime);
        var automations = new PluginAutomationCatalogRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(dispatch, automations);
        var features = new PluginFeatureSnapshotRegistry(dispatch, automations);
        var publisher = new PluginFeatureActivationPublisher(declarations);
        var context = new PluginLifecycleActivationContext(package.Installation, fence, package);

        _ = (
            await publisher.PublishAsync(context, CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();
        _ = PluginHostId.TryCreate(1, out var hostId);
        _ = PluginFeatureGeneration.TryCreate(1, out var featureGeneration);
        _ = PluginFeatureRevision.TryCreate(1, out var revision);
        foreach (var feature in package.PreparedPackage.Manifest!.Manifest.Features)
        {
            features.Publish(
                new(
                    new(package.Installation.PluginId, feature.Id, hostId),
                    fence,
                    featureGeneration,
                    new PluginFeatureReadiness.Ready(),
                    revision
                )
            );
        }
        var now = DateTimeOffset.UtcNow;
        _ = runtime.Publish(
            new(
                package.Installation.PluginId,
                package.Installation,
                operationId,
                workerGeneration,
                new(package.Installation, fence),
                PluginLifecyclePhase.Active,
                PluginLifecycleOperationKind.Activate,
                null,
                false,
                null,
                PluginLifecycleOutcome.Progress(PluginLifecycleOutcomeCode.Activated, now),
                1,
                now
            ),
            new PassiveLifecycleWorker()
        );

        automations.DescriptorsForHost(hostId.Value).ShouldNotBeEmpty();
        _ = PluginFeatureId.TryCreate("collection", out var collection);
        _ = new PluginPageCatalog(declarations, features, runtime)
            .Resolve(package.Installation.PluginId, collection, hostId, "queue-preview")
            .ShouldBeOfType<PluginPageResolution.Available>();

        await publisher.WithdrawAsync(context, CancellationToken.None);

        declarations.Current.Declarations.ShouldNotContainKey(package.Installation.PluginId);
        automations.DescriptorsForHost(hostId.Value).ShouldBeEmpty();
        _ = new PluginPageCatalog(declarations, features, runtime)
            .Resolve(package.Installation.PluginId, collection, hostId, "queue-preview")
            .ShouldBeOfType<PluginPageResolution.Missing>();
    }

    [Test]
    public async Task ApplicationBoundary_StagesOnceDelegatesLifecycleAndKeepsReceiptRedacted()
    {
        using var root = new TemporaryDirectory();
        var options = Options(root.Path);
        var clock = new ManualTimeProvider(_now);
        var database = await CreateDatabaseAsync(root.Path);
        using var registry = new PluginMarketplaceCatalogRegistry(
            new EfPluginMarketplaceCatalogStore(database),
            new QueueCatalogTransport(
                new PluginMarketplaceCatalogDownload.Delivered(Catalog(), null, null)
            ),
            clock
        );
        await registry.RefreshAsync(CancellationToken.None);
        var archive = new FixedArchiveTransport(Archive(PluginContractFixtures.CompletePackage()));
        var packages = new PluginMarketplacePackageStore(options, archive, new(), Runtime());
        var lifecycle = new RecordingLifecycleCoordinator();
        var receipts = new MemoryReceiptStore();
        var service = new PluginMarketplaceApplicationService(
            CreateCatalogService(registry, clock),
            packages,
            lifecycle,
            receipts,
            clock
        );
        var entry = Entry();

        _ = (
            await service.InstallAsync(
                Admin() with
                {
                    IsBotAdmin = false,
                },
                entry.PluginId,
                entry.Release,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginMarketplaceCommandOutcome.Rejected>();
        archive.Calls.ShouldBe(0);
        lifecycle.Activations.ShouldBe(0);

        var completed = (
            await service.InstallAsync(
                Admin(),
                entry.PluginId,
                entry.Release,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginMarketplaceCommandOutcome.Completed>();
        archive.Calls.ShouldBe(1);
        lifecycle.Activations.ShouldBe(1);
        var receipt = completed.Receipt.ShouldNotBeNull();
        receipt.Release.ShouldBe(entry.Release);
        receipt.OutcomeCode.ShouldBe("Activated");
        receipt.ToString().ShouldNotContain(lifecycle.OperationId.ToString());
        receipt.ToString().ShouldNotContain("36697a9e");
    }

    [Test]
    public async Task ExplicitUpdate_RedownloadsMovedTagIntoFreshOperationAndDropsOldPackage()
    {
        using var root = new TemporaryDirectory();
        var options = Options(root.Path);
        var clock = new ManualTimeProvider(_now);
        var database = await CreateDatabaseAsync(root.Path);
        using var registry = new PluginMarketplaceCatalogRegistry(
            new EfPluginMarketplaceCatalogStore(database),
            new QueueCatalogTransport(
                new PluginMarketplaceCatalogDownload.Delivered(Catalog(), null, null)
            ),
            clock
        );
        await registry.RefreshAsync(CancellationToken.None);
        var firstPackage = PluginContractFixtures.CompletePackage();
        var movedPackage = firstPackage
            .Select(entry =>
                entry is PluginPackageEntry.File { Path: "lua/main.lua" }
                    ? new PluginPackageEntry.File(
                        entry.Path,
                        Encoding.UTF8.GetBytes("return { moved = true }\n")
                    )
                    : entry
            )
            .ToArray();
        var archive = new QueueArchiveTransport(Archive(firstPackage), Archive(movedPackage), null);
        var packages = new PluginMarketplacePackageStore(options, archive, new(), Runtime());
        var lifecycle = new RecordingLifecycleCoordinator();
        var receipts = new MemoryReceiptStore();
        var service = new PluginMarketplaceApplicationService(
            CreateCatalogService(registry, clock),
            packages,
            lifecycle,
            receipts,
            clock
        );
        var entry = Entry();

        _ = (
            await service.InstallAsync(
                Admin(),
                entry.PluginId,
                entry.Release,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginMarketplaceCommandOutcome.Completed>();
        var first = lifecycle.Packages.ShouldHaveSingleItem();
        var firstRoot = first.PreparedPackage.PackageRoot;
        (await File.ReadAllTextAsync(Path.Combine(firstRoot, "lua/main.lua"))).ShouldBe(
            "return {}\n"
        );

        var updated = (
            await service.UpdateAsync(
                Admin(),
                entry.PluginId,
                entry.Release,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginMarketplaceCommandOutcome.Completed>();
        var second = lifecycle.Packages[1];

        lifecycle.Replacements.ShouldBe(1);
        archive.Calls.ShouldBe(2);
        second.Installation.ShouldBe(first.Installation);
        second.PreparedPackage.PackageRoot.ShouldNotBe(firstRoot);
        (
            await File.ReadAllTextAsync(
                Path.Combine(second.PreparedPackage.PackageRoot, "lua/main.lua")
            )
        ).ShouldBe("return { moved = true }\n");
        Directory.Exists(firstRoot).ShouldBeFalse();
        Directory.Exists(second.PreparedPackage.PackageRoot).ShouldBeTrue();
        updated.Receipt.ShouldNotBeNull().Release.ShouldBe(entry.Release);

        var missing = (
            await service.UpdateAsync(
                Admin(),
                entry.PluginId,
                entry.Release,
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginMarketplaceCommandOutcome.Rejected>();
        missing.Code.ShouldBe(PluginMarketplaceCommandRejectionCode.PackageDownloadFailed);
        missing.Receipt.ShouldNotBeNull().OutcomeCode.ShouldBe("package-download-failed");
        missing.Receipt.ToString().ShouldNotContain("github.com");
        lifecycle.Replacements.ShouldBe(1);
        Directory.Exists(second.PreparedPackage.PackageRoot).ShouldBeTrue();
    }

    [Test]
    public async Task DurableReceipt_IsDeletedByDestructiveRemoval()
    {
        using var root = new TemporaryDirectory();
        var databasePath = Path.Combine(root.Path, "blokebot.db");
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var context = factory.CreateDbContext())
        {
            _ = await context.Database.EnsureCreatedAsync();
        }

        var entry = Entry();
        var store = new EfPluginMarketplaceReceiptStore(factory);
        await store.SaveAsync(
            new(
                entry.PluginId,
                PluginMarketplaceOperationKind.Install,
                entry.Release,
                "Activated",
                null,
                _now
            ),
            CancellationToken.None
        );
        await store.SaveAsync(
            new(
                entry.PluginId,
                PluginMarketplaceOperationKind.Update,
                entry.Release,
                "Updated",
                null,
                _now.AddMinutes(1)
            ),
            CancellationToken.None
        );
        var latest = (
            await store.LoadAsync(entry.PluginId, CancellationToken.None)
        ).ShouldNotBeNull();
        latest.Operation.ShouldBe(PluginMarketplaceOperationKind.Update);
        latest.OutcomeCode.ShouldBe("Updated");
        _ = (
            await store.RemoveAsync(new(entry.PluginId, FixtureFence()), CancellationToken.None)
        ).ShouldBeOfType<PluginLifecycleOwnerOutcome.Succeeded>();

        var restarted = new EfPluginMarketplaceReceiptStore(new TestDbContextFactory(options));
        (await restarted.LoadAsync(entry.PluginId, CancellationToken.None)).ShouldBeNull();
    }

    private static PluginMarketplaceCatalogService CreateCatalogService(
        PluginMarketplaceCatalogRegistry registry,
        TimeProvider timeProvider
    ) => new(registry, timeProvider);

    private static async Task AssertConditionalRestartAsync(
        string? entityTag,
        DateTimeOffset? modifiedAt
    )
    {
        using var root = new TemporaryDirectory();
        var clock = new ManualTimeProvider(_now);
        var database = await CreateDatabaseAsync(root.Path);
        using (
            var registry = new PluginMarketplaceCatalogRegistry(
                new EfPluginMarketplaceCatalogStore(database),
                new QueueCatalogTransport(
                    new PluginMarketplaceCatalogDownload.Delivered(Catalog(), entityTag, modifiedAt)
                ),
                clock
            )
        )
        {
            await registry.InitializeAsync(CancellationToken.None);
            await registry.RefreshAsync(CancellationToken.None);
        }

        clock.Advance(TimeSpan.FromHours(1));
        var transport = new QueueCatalogTransport(
            new PluginMarketplaceCatalogDownload.NotModified(null, null)
        );
        using var restarted = new PluginMarketplaceCatalogRegistry(
            new EfPluginMarketplaceCatalogStore(new TestDbContextFactory(database.Options)),
            transport,
            clock
        );
        await restarted.InitializeAsync(CancellationToken.None);
        await restarted.RefreshAsync(CancellationToken.None);

        transport.Conditions.ShouldHaveSingleItem().ShouldBe((entityTag, modifiedAt));
        restarted.Current.LastValid.ShouldNotBeNull().RefreshedAt.ShouldBe(_now);
        restarted.Current.LastAttemptAt.ShouldBe(_now.AddHours(1));
        restarted.Current.RefreshFailure.ShouldBeNull();
        restarted.Current.SourceETag.ShouldBe(entityTag);
        restarted.Current.SourceModifiedAt.ShouldBe(modifiedAt);
    }

    private static async Task<TestDbContextFactory> CreateDatabaseAsync(string root)
    {
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "catalog.db")}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using var context = factory.CreateDbContext();
        _ = await context.Database.EnsureCreatedAsync();
        return factory;
    }

    private static PluginMarketplaceStorageOptions Options(string root) =>
        new(
            Path.Combine(root, "plugin-packages"),
            Path.Combine(root, "plugins"),
            TimeSpan.FromHours(1)
        );

    private static PluginMarketplaceRuntimeContext Runtime() =>
        new(
            PluginContractFixtures.CompatibleHost(),
            new UnavailableHostCalls(),
            NullLogger<PluginWorkerClient>.Instance
        );

    private static PluginMarketplaceCatalogEntry Entry()
    {
        var catalog = PluginMarketplaceCatalogParser
            .Validate(Catalog())
            .ShouldBeOfType<PluginMarketplaceCatalogValidationOutcome.Accepted>();
        return catalog.Entries.ShouldHaveSingleItem();
    }

    private static PluginLifecycleFence FixtureFence()
    {
        _ = PluginWorkerGeneration.TryCreate(1, out var generation);
        return new(PluginLifecycleOperationId.New(), generation);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private static AuthenticatedSession Admin() =>
        new()
        {
            IsAuthenticated = true,
            IsBotAdmin = true,
            IsBotAccount = true,
        };

    private static byte[] Catalog(
        string tag = "community-link-queue",
        string repository = "https://github.com/community/blokebot-plugins",
        int schemaVersion = 1,
        string extra = "",
        string blokeBot = ">=0.13.0 <0.14.0"
    ) =>
        Encoding.UTF8.GetBytes(
            $$$"""
            {
              "schemaVersion": {{{schemaVersion}}},
              "plugins": [{
                {{{extra}}}
                "id": "community.link-queue",
                "name": "Link queue",
                "summary": "Queue links from chat.",
                "author": "Community",
                "tags": ["queue", "chat"],
                "iconUrl": "https://images.example.test/icon.png",
                "mediaUrls": ["https://cdn.example.test/demo.webp"],
                "source": {
                  "repositoryUrl": "{{{repository}}}",
                  "packagePath": "plugins/link-queue"
                },
                "version": "1.2.0",
                "tag": "{{{tag}}}",
                "compatibility": {
                  "blokeBot": "{{{blokeBot}}}",
                  "pluginApi": "1",
                  "lua": "5.4",
                  "targets": ["linux-x64"]
                }
              }]
            }
            """
        );

    private static byte[] Archive(
        IReadOnlyList<PluginPackageEntry> package,
        int unrelatedEntries = 0
    )
    {
        using var target = new MemoryStream();
        using (var gzip = new GZipStream(target, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            for (var index = 0; index < unrelatedEntries; index++)
            {
                writer.WriteEntry(
                    new PaxTarEntry(
                        TarEntryType.RegularFile,
                        $"blokebot-plugins-fixture/unrelated/{index}.txt"
                    )
                    {
                        DataStream = new MemoryStream([], writable: false),
                    }
                );
            }

            foreach (var file in package.OfType<PluginPackageEntry.File>())
            {
                writer.WriteEntry(
                    new PaxTarEntry(
                        TarEntryType.RegularFile,
                        $"blokebot-plugins-fixture/plugins/link-queue/{file.Path}"
                    )
                    {
                        DataStream = new MemoryStream(file.Content.ToArray(), writable: false),
                    }
                );
            }
        }

        return target.ToArray();
    }

    private static byte[] AttackArchive(ArchiveAttack attack)
    {
        using var target = new MemoryStream();
        using (var gzip = new GZipStream(target, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            switch (attack)
            {
                case ArchiveAttack.SymbolicLink:
                    writer.WriteEntry(
                        new PaxTarEntry(
                            TarEntryType.SymbolicLink,
                            "blokebot-plugins-fixture/plugins/link-queue/link"
                        )
                        {
                            LinkName = "../../outside",
                        }
                    );
                    break;
                case ArchiveAttack.HardLink:
                    writer.WriteEntry(
                        new PaxTarEntry(
                            TarEntryType.HardLink,
                            "blokebot-plugins-fixture/plugins/link-queue/link"
                        )
                        {
                            LinkName = "blokebot-plugins-fixture/outside",
                        }
                    );
                    break;
                case ArchiveAttack.CaseCollision:
                    Write("blokebot-plugins-fixture/plugins/link-queue/A.lua");
                    Write("blokebot-plugins-fixture/plugins/link-queue/a.lua");
                    break;
                case ArchiveAttack.Traversal:
                    Write("blokebot-plugins-fixture/plugins/link-queue/../outside");
                    break;
                case ArchiveAttack.AbsolutePath:
                    Write("/blokebot-plugins-fixture/plugins/link-queue/outside");
                    break;
                case ArchiveAttack.WindowsAbsolutePath:
                    Write("C:/blokebot-plugins-fixture/plugins/link-queue/outside");
                    break;
            }

            void Write(string name) =>
                writer.WriteEntry(
                    new PaxTarEntry(TarEntryType.RegularFile, name)
                    {
                        DataStream = new MemoryStream([1], writable: false),
                    }
                );
        }

        return target.ToArray();
    }

    public enum ArchiveAttack
    {
        SymbolicLink,
        HardLink,
        CaseCollision,
        Traversal,
        AbsolutePath,
        WindowsAbsolutePath,
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];

        internal List<string?> ETags { get; } = [];

        internal List<DateTimeOffset?> ModifiedSince { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            ETags.Add(request.Headers.IfNoneMatch.SingleOrDefault()?.ToString());
            ModifiedSince.Add(request.Headers.IfModifiedSince);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class QueueCatalogTransport(params PluginMarketplaceCatalogDownload[] outcomes)
        : IPluginMarketplaceCatalogTransport
    {
        private readonly Queue<PluginMarketplaceCatalogDownload> _outcomes = new(outcomes);

        internal int Calls { get; private set; }

        internal List<(string? ETag, DateTimeOffset? ModifiedSince)> Conditions { get; } = [];

        public ValueTask<PluginMarketplaceCatalogDownload> DownloadAsync(
            string? entityTag,
            DateTimeOffset? modifiedSince,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            Conditions.Add((entityTag, modifiedSince));
            return ValueTask.FromResult(
                _outcomes.Count == 0
                    ? new PluginMarketplaceCatalogDownload.Failed()
                    : _outcomes.Dequeue()
            );
        }
    }

    private sealed class FixedArchiveTransport(ReadOnlyMemory<byte>? content)
        : IPluginMarketplaceArchiveTransport
    {
        internal int Calls { get; private set; }

        public async ValueTask<PluginMarketplaceArchiveDownload> DownloadAsync(
            Uri repository,
            PluginGitTag tag,
            string destination,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            if (content is not { } delivered)
            {
                return new PluginMarketplaceArchiveDownload.Failed();
            }

            await File.WriteAllBytesAsync(destination, delivered.ToArray(), cancellationToken);
            return new PluginMarketplaceArchiveDownload.Delivered();
        }
    }

    private sealed class QueueArchiveTransport(params byte[]?[] archives)
        : IPluginMarketplaceArchiveTransport
    {
        private readonly Queue<byte[]?> _archives = new(archives);

        internal int Calls { get; private set; }

        public async ValueTask<PluginMarketplaceArchiveDownload> DownloadAsync(
            Uri repository,
            PluginGitTag tag,
            string destination,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            var archive = _archives.Count == 0 ? null : _archives.Dequeue();
            if (archive is null)
            {
                return new PluginMarketplaceArchiveDownload.Failed();
            }

            await File.WriteAllBytesAsync(destination, archive, cancellationToken);
            return new PluginMarketplaceArchiveDownload.Delivered();
        }
    }

    private sealed class UnavailableHostCalls : IPluginHostCallDispatcher
    {
        public ValueTask<PluginHostCallOutcome> DispatchAsync(
            PluginHostCall call,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginHostCallOutcome>(
                new PluginHostCallOutcome.Failed(
                    new(PluginHostFailureCode.Unavailable, "Unavailable in marketplace test.")
                )
            );
    }

    private sealed class PassiveLifecycleWorker : IPluginLifecycleWorkerSession
    {
        public PluginWorkerMode Mode => PluginWorkerMode.Admitted;

        public Task<PluginWorkerFailure> Termination { get; } =
            new TaskCompletionSource<PluginWorkerFailure>().Task;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class ManualPeriodicTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            _ = timer.Change(dueTime, period);
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            _now += duration;
            foreach (var timer in _timers.ToArray())
            {
                timer.FireDue(_now);
            }
        }

        private sealed class ManualTimer(
            ManualPeriodicTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private DateTimeOffset? _dueAt;
            private TimeSpan _period;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                return true;
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal void FireDue(DateTimeOffset now)
            {
                if (_disposed || _dueAt is not { } dueAt || dueAt > now)
                {
                    return;
                }

                _dueAt = _period == Timeout.InfiniteTimeSpan ? null : dueAt + _period;
                callback(state);
            }
        }
    }

    private sealed class MemoryReceiptStore : IPluginMarketplaceReceiptStore
    {
        private readonly Dictionary<PluginId, PluginMarketplaceReceipt> _receipts = [];

        public ValueTask<PluginMarketplaceReceipt?> LoadAsync(
            PluginId pluginId,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult(_receipts.TryGetValue(pluginId, out var receipt) ? receipt : null);

        public ValueTask SaveAsync(
            PluginMarketplaceReceipt receipt,
            CancellationToken cancellationToken
        )
        {
            _receipts[receipt.PluginId] = receipt;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLifecycleCoordinator : IPluginLifecycleCoordinator
    {
        internal int Activations { get; private set; }
        internal int Replacements { get; private set; }
        internal List<PluginLifecyclePackage> Packages { get; } = [];
        private ulong _generation;
        internal PluginLifecycleOperationId OperationId { get; private set; } =
            PluginLifecycleOperationId.New();

        public ValueTask<PluginLifecycleCommandOutcome> ActivateAsync(
            PluginLifecycleOperationId operationId,
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        )
        {
            Activations++;
            Packages.Add(package);
            OperationId = operationId;
            _ = PluginWorkerGeneration.TryCreate(++_generation, out var generation);
            var outcome = PluginLifecycleOutcome.Progress(
                PluginLifecycleOutcomeCode.Activated,
                _now
            );
            return ValueTask.FromResult<PluginLifecycleCommandOutcome>(
                new PluginLifecycleCommandOutcome.Succeeded(
                    new(
                        package.Installation,
                        PluginLifecyclePhase.Active,
                        operationId,
                        generation,
                        outcome,
                        false
                    )
                )
            );
        }

        public ValueTask<PluginLifecycleCommandOutcome> ReplaceAsync(
            PluginLifecycleOperationId operationId,
            PluginLifecyclePackage package,
            CancellationToken cancellationToken
        )
        {
            Replacements++;
            return ActivateAsync(operationId, package, cancellationToken);
        }

        public ValueTask<PluginLifecycleCommandOutcome> RemoveAsync(
            PluginId pluginId,
            PluginLifecycleOperationId operationId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PluginLifecycleCommandOutcome> RestartAsync(
            PluginId pluginId,
            PluginLifecycleOperationId operationId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask RecoverAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<BlokeBotDbContext>
    {
        internal TestDbContextFactory(DbContextOptions<BlokeBotDbContext> options) =>
            Options = options;

        internal DbContextOptions<BlokeBotDbContext> Options { get; }

        public BlokeBotDbContext CreateDbContext() => new(Options);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"blokebot-marketplace-{Guid.NewGuid():N}"
            );
            _ = Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
