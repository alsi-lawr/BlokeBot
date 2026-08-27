using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.BotStatus;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.Admin;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginAdminTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NonBotAdmin_LoadingOrMutating_CannotReadStateOrStartACommand()
    {
        var lifecycles = new RecordingLifecycleStore([]);
        var service = CreateApplicationService(
            lifecycles,
            new MemoryReceiptStore(),
            PluginMarketplaceCatalogStateWithoutSnapshot()
        );
        var session = new AuthenticatedSession { IsAuthenticated = true, IsBotAdmin = false };
        var entry = CatalogEntry();

        var load = await service.LoadAsync(session, null, CancellationToken.None);
        var install = await service.InstallAsync(
            session,
            entry.PluginId,
            entry.Release,
            CancellationToken.None
        );
        var update = await service.UpdateAsync(
            session,
            entry.PluginId,
            entry.Release,
            CancellationToken.None
        );

        _ = load.ShouldBeOfType<PluginAdminLoadOutcome.Unauthorized>();
        install
            .ShouldBeOfType<PluginMarketplaceCommandOutcome.Rejected>()
            .Code.ShouldBe(PluginMarketplaceCommandRejectionCode.Unauthorized);
        update
            .ShouldBeOfType<PluginMarketplaceCommandOutcome.Rejected>()
            .Code.ShouldBe(PluginMarketplaceCommandRejectionCode.Unauthorized);
        lifecycles.LoadAllCalls.ShouldBe(0);
    }

    [Test]
    public async Task BotAccount_LoadingAdminView_ProjectsInstalledStateAndSearchesOnlySavedCatalogue()
    {
        var active = Lifecycle("community.link-queue", PluginLifecyclePhase.Active);
        var faulted = Lifecycle("community.alerts", PluginLifecyclePhase.Faulted) with
        {
            LatestOutcome = PluginLifecycleOutcome.Failure(
                PluginLifecycleFailureCode.WorkerExited,
                null,
                _now
            ),
        };
        var lifecycles = new RecordingLifecycleStore([active, faulted]);
        var receipts = new MemoryReceiptStore();
        var receipt = new PluginMarketplaceReceipt(
            active.PluginId,
            PluginMarketplaceOperationKind.Update,
            active.SelectedInstallation.Release,
            "MigrationFailed",
            "Migration step 2 failed.",
            _now
        );
        await receipts.SaveAsync(receipt, CancellationToken.None);
        var transport = new RejectingCatalogTransport();
        var service = CreateApplicationService(
            lifecycles,
            receipts,
            new(
                new(1, _now.AddMinutes(-15), [CatalogEntry()]),
                _now.AddMinutes(-1),
                PluginMarketplaceRefreshFailureCode.DownloadFailed,
                null,
                null
            ),
            transport
        );

        var outcome = await service.LoadAsync(
            new()
            {
                IsAuthenticated = true,
                IsBotAdmin = true,
                IsBotAccount = true,
            },
            "queue",
            CancellationToken.None
        );

        var snapshot = outcome.ShouldBeOfType<PluginAdminLoadOutcome.Loaded>().Snapshot;
        snapshot.Installed.Length.ShouldBe(2);
        snapshot
            .Installed.Single(plugin => plugin.PluginId == active.PluginId)
            .LatestReceipt.ShouldBe(receipt);
        snapshot
            .Installed.Single(plugin => plugin.PluginId == faulted.PluginId)
            .Status.ShouldBe(PluginAdminInstalledStatus.Faulted);
        snapshot
            .Catalog.ShouldBeOfType<PluginAdminCatalog.Available>()
            .Entries.ShouldHaveSingleItem()
            .Entry.PluginId.ShouldBe(active.PluginId);
        transport.Calls.ShouldBe(0);
    }

    [Test]
    public async Task InstalledInventory_SelectsTheCurrentCompatibleReleaseDespiteSearchFiltering()
    {
        var active = Lifecycle("community.link-queue", PluginLifecyclePhase.Active);
        var older = CatalogEntry() with { Release = Release("0.9.0", "release-v0.9") };
        var incompatibleNewer = CatalogEntry() with
        {
            Release = Release("2.0.0", "release-v2"),
            Compatibility = new(">=0.13.0 <0.14.0", "1", "5.4", ["windows-x64"]),
        };
        var service = CreateApplicationService(
            new RecordingLifecycleStore([active]),
            new MemoryReceiptStore(),
            new(new(1, _now, [CatalogEntry(), older, incompatibleNewer]), _now, null, null, null)
        );

        var outcome = await service.LoadAsync(AdminSession(), "no-match", CancellationToken.None);

        var snapshot = outcome.ShouldBeOfType<PluginAdminLoadOutcome.Loaded>().Snapshot;
        var installed = snapshot.Installed.ShouldHaveSingleItem();
        installed.Name.ShouldBe("Link queue");
        installed.UpdateRelease.ShouldBe(CatalogEntry().Release);
        snapshot.Catalog.ShouldBeOfType<PluginAdminCatalog.Available>().Entries.ShouldBeEmpty();
    }

    [Test]
    public void SavedCatalogueSearch_ChangingTextLoadsTheLocalProjectionImmediately()
    {
        using var context = CreateComponentContext(
            Snapshot(installed: [], catalogEntries: [new(CatalogEntry(), true, false, false)])
        );
        var service = context.Services.GetRequiredService<RecordingAdminApplicationService>();
        var panel = context.Render<PluginAdminPanel>(parameters =>
            parameters.Add(value => value.Session, AdminSession())
        );

        panel.Find("#plugin-catalogue-search").Input("alerts");

        service.LoadQueries.ShouldBe([string.Empty, "alerts"]);
    }

    [Test]
    public async Task AdminPluginFragment_DirectLoadAndHistoryNavigationKeepTheSelectedPanelInSync()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var events = TestEventBus.Create<AppEventKind>();
        var admins = new BotAdminService(
            BotAdminSettings.FromOptions(new BlokeBotOptions { BotAdmins = ["botaccount"] })
        );
        _ = context.Services.AddSingleton<
            IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext>
        >(database);
        _ = context.Services.AddSingleton(events);
        _ = context.Services.AddSingleton(new BlokeBotPageContextAccessor());
        _ = context.Services.AddSingleton<UiFaultTelemetry>();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        _ = context.Services.AddScoped<ToastService>();
        _ = context.Services.AddSingleton<IBotRuntimeStatusAccessor>(
            new OfflineBotStatusAccessor()
        );
        _ = context.Services.AddSingleton(admins);
        _ = context.Services.AddSingleton(
            new SiteAccessService(database, admins, new SiteAccessChangeNotifier(events))
        );
        _ = context.Services.AddSingleton(new HostedChannelDirectoryService(database));
        _ = context.Services.AddSingleton(
            RuntimeHelpers.GetUninitializedObject(typeof(AdminHostManagementService))
                as AdminHostManagementService
                ?? throw new InvalidOperationException("The Admin service fixture was not created.")
        );
        var botSettings = BotSettings.FromOptions(
            new BotOptions { Identity = new BotIdentityOptions { BotUsername = "botaccount" } }
        );
        _ = context.Services.AddSingleton(
            new BotAccountAuthorizationService(
                new DisabledBotAccountAuthorizationPolicy(botSettings)
            )
        );
        _ = context.Services.AddSingleton(
            new AccessListProfileResolver(new DisabledAccessListProfileEnrichmentPolicy())
        );
        var pluginService = new RecordingAdminApplicationService(Snapshot([], []), null);
        _ = context.Services.AddSingleton<IPluginAdminApplicationService>(pluginService);
        var authorization = context.AddAuthorization();
        _ = authorization.SetAuthorized("Bot Account");
        _ = authorization.SetPolicies("BotAdmin");
        _ = authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "bot-id"),
            new Claim(ClaimTypes.Name, "Bot Account"),
            new Claim(AuthClaims.Login, "botaccount"),
            new Claim(AuthClaims.Role, AuthRoleCodec.Encode(AuthRole.Bot)),
            new Claim(AuthClaims.IsBotAdmin, "true"),
            new Claim(AuthClaims.IsBotAccount, "true")
        );
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/admin#plugins");

        var page = context.Render<AdminPage>();

        page.WaitForAssertion(() =>
        {
            page.Find("#admin-plugins-tab").GetAttribute("aria-selected").ShouldBe("true");
            page.Find("#admin-plugins-panel").GetAttribute("hidden").ShouldBeNull();
            _ = pluginService.LoadQueries.ShouldHaveSingleItem();
        });

        page.Find("#admin-administration-tab").Click();
        navigation.Uri.ShouldEndWith("/admin#administration");
        page.Find("#admin-administration-tab").GetAttribute("aria-selected").ShouldBe("true");

        navigation.NavigateTo("/admin#plugins");
        page.Find("#admin-plugins-tab").GetAttribute("aria-selected").ShouldBe("true");
        page.Find("#admin-plugins-panel").GetAttribute("hidden").ShouldBeNull();
    }

    [Test]
    public void NonBotAdmin_RoutingToPluginAdmin_CannotLoadThePage()
    {
        using var context = new BunitContext();
        var authorization = context.AddAuthorization();
        _ = authorization.SetAuthorized("channel owner");
        var plugins = new RecordingAdminApplicationService(Snapshot([], []), null);
        _ = context.Services.AddSingleton<IPluginAdminApplicationService>(plugins);
        var routeData = new RouteData(typeof(AdminPage), new Dictionary<string, object?>());

        var route = context.Render<AuthorizeRouteView>(parameters =>
            parameters.Add(value => value.RouteData, routeData)
        );

        route.FindAll("[data-plugin-admin]").ShouldBeEmpty();
        plugins.LoadQueries.ShouldBeEmpty();
    }

    [Test]
    public void DestructiveRemoval_RequiresThePluginIdAndReloadsWithoutAReceipt()
    {
        var plugin = InstalledPlugin();
        using var context = CreateComponentContext(
            Snapshot([plugin], []),
            afterRemove: Snapshot([], [])
        );
        var service = context.Services.GetRequiredService<RecordingAdminApplicationService>();
        var panel = context.Render<PluginAdminPanel>(parameters =>
            parameters.Add(value => value.Session, AdminSession())
        );

        panel.Find("[data-installed-plugin] .plugin-admin__danger-button").Click();
        var dialog = panel.Find("[data-plugin-confirmation-dialog]");
        dialog.TextContent.ShouldContain("The package files.");
        dialog.TextContent.ShouldContain(
            "The installation, settings, features, configuration, and secrets."
        );
        dialog.TextContent.ShouldContain("The schedules and private data.");
        dialog.TextContent.ShouldContain("The automation definitions and ledgers.");
        dialog.TextContent.ShouldContain(
            "Every plugin-dependent flow, node, and item of run history."
        );
        dialog.TextContent.ShouldContain("The marketplace receipts.");
        dialog.TextContent.ShouldContain("All other plugin context.");
        dialog.TextContent.ShouldContain("The catalogue metadata remains.");
        dialog.TextContent.ShouldNotContain("Purge");
        var confirm =
            dialog.QuerySelector(".plugin-admin__danger-button")
            ?? throw new InvalidOperationException(
                "The permanent removal control was not rendered."
            );
        confirm.HasAttribute("disabled").ShouldBeTrue();
        panel.Find("#plugin-removal-confirmation").Input("wrong.plugin");
        panel
            .Find("[data-plugin-confirmation-dialog] .plugin-admin__danger-button")
            .HasAttribute("disabled")
            .ShouldBeTrue();

        panel.Find("#plugin-removal-confirmation").Input(plugin.PluginId.Value);
        panel.Find("[data-plugin-confirmation-dialog] .plugin-admin__danger-button").Click();

        service.Removals.ShouldBe([plugin.PluginId]);
        panel.FindAll("[data-installed-plugin]").ShouldBeEmpty();
        panel.FindAll("[data-durable-plugin-outcome]").ShouldBeEmpty();
    }

    [Test]
    public void Installation_OnlyTheConfirmedCatalogueReleaseStartsTheCommand()
    {
        var entry = new PluginAdminCatalogEntry(CatalogEntry(), true, false, false);
        using var context = CreateComponentContext(Snapshot([], [entry]));
        var service = context.Services.GetRequiredService<RecordingAdminApplicationService>();
        var panel = context.Render<PluginAdminPanel>(parameters =>
            parameters.Add(value => value.Session, AdminSession())
        );

        panel.Find("[data-catalog-plugin] .btn-primary").Click();
        panel
            .FindAll("[data-plugin-confirmation-dialog] #plugin-confirmation-description p")
            .ShouldHaveSingleItem()
            .TextContent.ShouldBe("Install version 1.0.0 from tag release-v1.");
        service.Installations.ShouldBeEmpty();
        panel.Find("[data-plugin-confirmation-dialog] .btn-secondary").Click();
        service.Installations.ShouldBeEmpty();

        panel.Find("[data-catalog-plugin] .btn-primary").Click();
        panel.Find("[data-plugin-confirmation-dialog] .btn-primary").Click();

        service.Installations.ShouldBe([(entry.Entry.PluginId, entry.Entry.Release)]);
    }

    [Test]
    public void CurrentMutableTagUpdate_RequiresConfirmationAndDelegatesTheExactRelease()
    {
        var plugin = InstalledPlugin();
        plugin = plugin with { UpdateRelease = plugin.Lifecycle.Installation.Release };
        using var context = CreateComponentContext(Snapshot([plugin], []));
        var service = context.Services.GetRequiredService<RecordingAdminApplicationService>();
        var panel = context.Render<PluginAdminPanel>(parameters =>
            parameters.Add(value => value.Session, AdminSession())
        );

        panel
            .FindAll("[data-installed-plugin] button")
            .Single(button => button.TextContent == "Update")
            .Click();
        panel
            .FindAll("[data-plugin-confirmation-dialog] #plugin-confirmation-description p")
            .ShouldHaveSingleItem()
            .TextContent.ShouldBe("Apply version 1.0.0 from mutable tag release-v1.");
        service.Updates.ShouldBeEmpty();

        panel.Find("[data-plugin-confirmation-dialog] .btn-primary").Click();

        service.Updates.ShouldBe([(plugin.PluginId, plugin.Lifecycle.Installation.Release)]);
        service.LoadQueries.ShouldBe([string.Empty, string.Empty]);
    }

    [Test]
    public void ActiveLifecycleOperation_DisablesEveryConflictingControl()
    {
        var plugin = InstalledPlugin(PluginLifecyclePhase.Migrating);
        plugin = plugin with { UpdateRelease = plugin.Lifecycle.Installation.Release };
        using var context = CreateComponentContext(Snapshot([plugin], []));
        var panel = context.Render<PluginAdminPanel>(parameters =>
            parameters.Add(value => value.Session, AdminSession())
        );

        var card = panel.Find("[data-installed-plugin]");
        card.QuerySelectorAll("[data-plugin-lifecycle-control]")
            .ShouldAllBe(button => button.HasAttribute("disabled"));
    }

    private static PluginAdminApplicationService CreateApplicationService(
        IPluginLifecycleStore lifecycles,
        IPluginMarketplaceReceiptStore receipts,
        PluginMarketplaceCatalogState catalogState,
        IPluginMarketplaceCatalogTransport? transport = null
    )
    {
        var clock = new FixedTimeProvider(_now);
        var registry = new PluginMarketplaceCatalogRegistry(
            new StaticCatalogStore(catalogState),
            transport ?? new RejectingCatalogTransport(),
            clock
        );
        registry.InitializeAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        var catalogue = new PluginMarketplaceCatalogService(registry, clock);
        var marketplace = new PluginMarketplaceApplicationService(
            null!,
            null!,
            null!,
            null!,
            null!
        );
        return new(
            catalogue,
            new(
                PluginContractFixtures.CompatibleHost(),
                null!,
                NullLogger<PluginWorkerClient>.Instance
            ),
            lifecycles,
            receipts,
            new PluginFeatureDeclarationRegistry(),
            new PluginFeatureSnapshotRegistry(),
            marketplace
        );
    }

    private static BunitContext CreateComponentContext(
        PluginAdminSnapshot initial,
        PluginAdminSnapshot? afterRemove = null
    )
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var service = new RecordingAdminApplicationService(initial, afterRemove);
        _ = context.Services.AddSingleton(service);
        _ = context.Services.AddSingleton<IPluginAdminApplicationService>(service);
        _ = context.Services.AddBlokeBotToasts();
        return context;
    }

    private static PluginAdminSnapshot Snapshot(
        ImmutableArray<PluginAdminInstalledPlugin> installed,
        ImmutableArray<PluginAdminCatalogEntry> catalogEntries
    ) =>
        new(
            installed,
            new PluginAdminCatalog.Available(
                catalogEntries,
                _now.AddMinutes(-10),
                TimeSpan.FromMinutes(10),
                null
            )
        );

    private static PluginAdminInstalledPlugin InstalledPlugin(
        PluginLifecyclePhase phase = PluginLifecyclePhase.Active
    )
    {
        var state = Lifecycle("community.link-queue", phase);
        return new(
            state.PluginId,
            "Link queue",
            PluginLifecycleView.From(state),
            phase == PluginLifecyclePhase.Faulted ? PluginAdminInstalledStatus.Faulted
                : phase == PluginLifecyclePhase.Active ? PluginAdminInstalledStatus.Active
                : PluginAdminInstalledStatus.Operation,
            2,
            [],
            null,
            new(
                state.PluginId,
                PluginMarketplaceOperationKind.Install,
                state.SelectedInstallation.Release,
                "Activated",
                null,
                _now
            )
        );
    }

    private static PluginLifecycleState Lifecycle(string id, PluginLifecyclePhase phase)
    {
        _ = PluginId.TryCreate(id, out var pluginId);
        _ = PluginWorkerGeneration.TryCreate(1, out var generation);
        var operationId = PluginLifecycleOperationId.New();
        var release = Release("1.0.0", "release-v1");
        var operationKind =
            phase == PluginLifecyclePhase.Removing
                ? PluginLifecycleOperationKind.Remove
                : PluginLifecycleOperationKind.Activate;
        return new(
            pluginId,
            new(pluginId, release),
            PluginPackageOperationId.New(),
            operationId,
            generation,
            null,
            phase,
            operationKind,
            phase == PluginLifecyclePhase.Faulted ? PluginLifecyclePhase.Active : null,
            false,
            null,
            PluginLifecycleOutcome.Progress(PluginLifecycleOutcomeCode.Activated, _now),
            1,
            _now
        );
    }

    private static PluginMarketplaceCatalogEntry CatalogEntry()
    {
        _ = PluginId.TryCreate("community.link-queue", out var pluginId);
        return new(
            pluginId,
            "Link queue",
            "Queue links from chat.",
            "Community",
            ["queue", "chat"],
            null,
            [],
            new("https://github.com/community/blokebot-plugins"),
            "plugins/link-queue",
            Release("1.0.0", "release-v1"),
            new(">=0.13.0 <0.14.0", "1", "5.4", ["linux-x64"])
        );
    }

    private static PluginReleaseIdentity Release(string versionValue, string tagValue)
    {
        _ = SemanticVersion.TryCreate(versionValue, out var version);
        _ = PluginGitTag.TryCreate(tagValue, out var tag);
        return new(version, tag);
    }

    private static AuthenticatedSession AdminSession() =>
        new()
        {
            IsAuthenticated = true,
            IsBotAdmin = true,
            IsBotAccount = true,
        };

    private static PluginMarketplaceCatalogState PluginMarketplaceCatalogStateWithoutSnapshot() =>
        new(null, _now, PluginMarketplaceRefreshFailureCode.DownloadFailed, null, null);

    private sealed class RecordingAdminApplicationService(
        PluginAdminSnapshot initial,
        PluginAdminSnapshot? afterRemove
    ) : IPluginAdminApplicationService
    {
        private PluginAdminSnapshot _current = initial;

        internal List<string?> LoadQueries { get; } = [];
        internal List<(PluginId PluginId, PluginReleaseIdentity Release)> Installations { get; } =
        [];
        internal List<(PluginId PluginId, PluginReleaseIdentity Release)> Updates { get; } = [];
        internal List<PluginId> Removals { get; } = [];

        public ValueTask<PluginAdminLoadOutcome> LoadAsync(
            AuthenticatedSession session,
            string? catalogQuery,
            CancellationToken cancellationToken
        )
        {
            LoadQueries.Add(catalogQuery);
            return ValueTask.FromResult<PluginAdminLoadOutcome>(
                new PluginAdminLoadOutcome.Loaded(_current)
            );
        }

        public ValueTask<PluginMarketplaceCommandOutcome> InstallAsync(
            AuthenticatedSession session,
            PluginId pluginId,
            PluginReleaseIdentity release,
            CancellationToken cancellationToken
        )
        {
            Installations.Add((pluginId, release));
            var state = Lifecycle(pluginId.Value, PluginLifecyclePhase.Active);
            return ValueTask.FromResult<PluginMarketplaceCommandOutcome>(
                new PluginMarketplaceCommandOutcome.Completed(
                    new PluginLifecycleCommandOutcome.Succeeded(PluginLifecycleView.From(state)),
                    null
                )
            );
        }

        public ValueTask<PluginMarketplaceCommandOutcome> UpdateAsync(
            AuthenticatedSession session,
            PluginId pluginId,
            PluginReleaseIdentity release,
            CancellationToken cancellationToken
        )
        {
            Updates.Add((pluginId, release));
            var state = Lifecycle(pluginId.Value, PluginLifecyclePhase.Active);
            return ValueTask.FromResult<PluginMarketplaceCommandOutcome>(
                new PluginMarketplaceCommandOutcome.Completed(
                    new PluginLifecycleCommandOutcome.Succeeded(PluginLifecycleView.From(state)),
                    null
                )
            );
        }

        public ValueTask<PluginMarketplaceCommandOutcome> RestartAsync(
            AuthenticatedSession session,
            PluginId pluginId,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Restart is not expected.");

        public ValueTask<PluginMarketplaceCommandOutcome> RemoveAsync(
            AuthenticatedSession session,
            PluginId pluginId,
            CancellationToken cancellationToken
        )
        {
            Removals.Add(pluginId);
            _current = afterRemove ?? _current;
            return ValueTask.FromResult<PluginMarketplaceCommandOutcome>(
                new PluginMarketplaceCommandOutcome.Completed(
                    new PluginLifecycleCommandOutcome.Removed(pluginId),
                    null
                )
            );
        }
    }

    private sealed class RecordingLifecycleStore(IReadOnlyList<PluginLifecycleState> states)
        : IPluginLifecycleStore
    {
        internal int LoadAllCalls { get; private set; }

        public ValueTask<PluginLifecycleState?> LoadAsync(
            PluginId pluginId,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginLifecycleState?>(
                states.SingleOrDefault(state => state.PluginId == pluginId)
            );

        public ValueTask<IReadOnlyList<PluginLifecycleState>> LoadAllAsync(
            CancellationToken cancellationToken
        )
        {
            LoadAllCalls++;
            return ValueTask.FromResult(states);
        }

        public ValueTask<PluginLifecycleStoreBeginOutcome> BeginActivationAsync(
            PluginLifecycleBeginRequest request,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("A query cannot activate a plugin.");

        public ValueTask<PluginLifecycleStoreBeginOutcome> BeginReplacementAsync(
            PluginLifecycleBeginRequest request,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("A query cannot replace a plugin.");

        public ValueTask<PluginLifecycleStoreWriteOutcome> WriteAsync(
            PluginLifecycleState expected,
            PluginLifecycleState next,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("A query cannot write plugin state.");

        public ValueTask<PluginLifecycleStoreRemovalOutcome> CompleteRemovalAsync(
            PluginLifecycleState expected,
            PluginLifecycleOutcome outcome,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("A query cannot remove a plugin.");
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

    private sealed class StaticCatalogStore(PluginMarketplaceCatalogState state)
        : IPluginMarketplaceCatalogStore
    {
        public ValueTask<PluginMarketplaceCatalogState> LoadAsync(
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(state);

        public ValueTask<PluginMarketplaceCatalogState> ReplaceAsync(
            PluginMarketplaceCatalogSnapshot snapshot,
            DateTimeOffset attemptedAt,
            string? sourceETag,
            DateTimeOffset? sourceModifiedAt,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "The saved catalogue must not refresh in this test."
            );

        public ValueTask<PluginMarketplaceCatalogState> RecordNotModifiedAsync(
            DateTimeOffset attemptedAt,
            string? sourceETag,
            DateTimeOffset? sourceModifiedAt,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "The saved catalogue must not refresh in this test."
            );

        public ValueTask<PluginMarketplaceCatalogState> RecordFailureAsync(
            DateTimeOffset attemptedAt,
            PluginMarketplaceRefreshFailureCode failure,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "The saved catalogue must not refresh in this test."
            );
    }

    private sealed class RejectingCatalogTransport : IPluginMarketplaceCatalogTransport
    {
        internal int Calls { get; private set; }

        public ValueTask<PluginMarketplaceCatalogDownload> DownloadAsync(
            string? entityTag,
            DateTimeOffset? modifiedSince,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            throw new InvalidOperationException("Admin search must not use the network.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
