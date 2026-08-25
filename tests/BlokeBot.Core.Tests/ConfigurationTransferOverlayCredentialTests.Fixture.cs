using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferOverlayCredentialTests
{
    private static ConfigurationDocumentV1 Document(params string[] sourceNames) =>
        new(
            ConfigurationDocumentCodec.Format,
            ConfigurationDocumentCodec.CurrentVersion,
            DateTimeOffset.UtcNow,
            new("source", "0.12.0"),
            new(
                Overlays: new(
                    false,
                    false,
                    [
                        .. sourceNames.Select(
                            (name, index) =>
                                new OverlayInstanceV1(
                                    $"overlay-{index + 1}",
                                    name,
                                    OverlayType.Empty,
                                    true,
                                    new(1)
                                )
                        ),
                    ],
                    [],
                    [],
                    [],
                    []
                )
            )
        );

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly OverlayMediaMaintenanceService _mediaMaintenance;

        private Fixture(
            SqliteBlokeBotDbFactory database,
            int hostId,
            AuthenticatedSession session,
            DurableAlertService alerts,
            ConfigurationTransferCoordinator coordinator,
            OverlayInstanceService overlayService,
            OverlayInstanceResolver resolver,
            OverlayMediaMaintenanceService mediaMaintenance
        )
        {
            Database = database;
            HostId = hostId;
            Session = session;
            Alerts = alerts;
            Coordinator = coordinator;
            OverlayService = overlayService;
            Resolver = resolver;
            _mediaMaintenance = mediaMaintenance;
        }

        internal SqliteBlokeBotDbFactory Database { get; }
        internal int HostId { get; }
        internal AuthenticatedSession Session { get; }
        internal DurableAlertService Alerts { get; }
        internal ConfigurationTransferCoordinator Coordinator { get; }
        internal OverlayInstanceService OverlayService { get; }
        internal OverlayInstanceResolver Resolver { get; }

        internal static async Task<Fixture> CreateAsync(params IInterceptor[] interceptors)
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync(interceptors);
            int hostId;
            await using (var db = await database.CreateDbContextAsync())
            {
                var host = new BotHost
                {
                    TwitchUserId = "destination-id",
                    Login = "destination",
                    DisplayName = "Destination",
                    EnabledFeatures = HostFeatureFlags.Overlays,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                _ = db.Hosts.Add(host);
                _ = await db.SaveChangesAsync();
                hostId = host.Id;
            }

            var session = CreateSession(hostId);
            var time = new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero)
            );
            var events = TestEventBus.Create<AppEventKind>();
            var alerts = new DurableAlertService(database, time, events);
            var options = Options.Create(
                new BlokeBotOptions
                {
                    DatabasePath = Path.Combine(
                        Path.GetTempPath(),
                        $"blokebot-b263-{Guid.NewGuid():N}",
                        "state.db"
                    ),
                }
            );
            var mediaMaintenance = new OverlayMediaMaintenanceService(
                database,
                options,
                new SystemOverlayMediaFileDeletion(),
                time,
                NullLogger<OverlayMediaMaintenanceService>.Instance
            );
            var overlayAdapter = new OverlayConfigurationTransferAdapter(null!, options, time);
            var writer = new CustomCommandConfigurationGraphWriter(database, null!, time);
            var observers = new ConfigurationImportObserverDispatcher(
                [
                    new OverlayConfigurationImportObserver(
                        database,
                        alerts,
                        events,
                        mediaMaintenance
                    ),
                ],
                NullLogger<ConfigurationImportObserverDispatcher>.Instance
            );
            var coordinator = new ConfigurationTransferCoordinator(
                database,
                new(writer, new(), time),
                new GrantedAuthority(),
                new(),
                time,
                NullLogger<ConfigurationTransferCoordinator>.Instance,
                new(
                    database,
                    overlayAdapter,
                    UnavailableAutomationConfigurationTransferAdapter.Instance
                ),
                overlayAdapter,
                UnavailableAutomationConfigurationTransferAdapter.Instance,
                observers,
                mediaMaintenance.Gate
            );
            var overlayService = new OverlayInstanceService(
                database,
                new GrantedAuthority(),
                new SequentialAccessKeyGenerator(),
                alerts,
                events,
                time,
                NullLogger<OverlayInstanceService>.Instance
            );
            return new(
                database,
                hostId,
                session,
                alerts,
                coordinator,
                overlayService,
                new(database),
                mediaMaintenance
            );
        }

        internal async Task<ConfigurationImportApplied> ImportAsync(
            ConfigurationDocumentV1 document
        ) =>
            (await ImportOutcomeAsync(document, CancellationToken.None))
                .ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>()
                .Result;

        internal Task<ConfigurationImportApplyOutcome> ImportOutcomeAsync(
            ConfigurationDocumentV1 document,
            CancellationToken cancellationToken
        ) =>
            Coordinator.ApplyAsync(
                Session,
                document,
                new(
                    HostId,
                    [new(ConfigurationSectionId.Overlays, ImportConflictStrategy.Merge, [])],
                    new HashSet<HostFeatureFlags>()
                ),
                new("destination-id", "destination"),
                cancellationToken
            );

        internal async Task SeedOverlayAsync(
            string name,
            byte[] accessKeyDigest,
            bool requiresRegeneration
        )
        {
            await using var db = await Database.CreateDbContextAsync();
            var now = DateTime.UtcNow;
            _ = db.OverlayInstances.Add(
                new()
                {
                    PublicId = Guid.NewGuid(),
                    HostId = HostId,
                    Name = name,
                    Type = OverlayType.Empty,
                    IsEnabled = true,
                    ConfigurationJson = """{"schemaVersion":1}""",
                    AccessKeyDigest = accessKeyDigest,
                    RequiresAccessKeyRegeneration = requiresRegeneration,
                    KeyVersion = 1,
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
            _ = await db.SaveChangesAsync();
        }

        internal async Task<OverlayInstance> LoadOnlyOverlayAsync()
        {
            await using var db = await Database.CreateDbContextAsync();
            return await db.OverlayInstances.AsNoTracking().SingleAsync();
        }

        internal async Task<IReadOnlyList<OverlayInstanceView>> ListOverlaysAsync() =>
            (await OverlayService.ListAsync(Session, CancellationToken.None)).SucceededValue();

        internal async Task<ConfigurationExportOutcome.Success> ExportAsync()
        {
            var automation = ConfigurationTransferAutomationTestServices.Create(Database);
            var exporter = new ConfigurationDocumentExporter(
                Database,
                new(),
                automation.Catalog,
                automation.Flows,
                NullLogger<ConfigurationDocumentExporter>.Instance,
                TimeProvider.System,
                new EfPluginFeatureStore(Database, new())
            );
            return (
                await exporter.ExportAsync(
                    HostId,
                    new(
                        new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Overlays },
                        new(false, false, false)
                    ),
                    CancellationToken.None
                )
            ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        }

        public async ValueTask DisposeAsync()
        {
            _mediaMaintenance.Dispose();
            Alerts.Dispose();
            await Database.DisposeAsync();
        }

        private static AuthenticatedSession CreateSession(int hostId)
        {
            var host = new BotHostChoice(hostId, "destination", "Destination", AuthRole.Streamer);
            return new()
            {
                IsAuthenticated = true,
                UserId = "destination-id",
                Login = "destination",
                State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
            };
        }
    }

    private sealed class GrantedAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }

    private sealed class SequentialAccessKeyGenerator : IOverlayAccessKeyGenerator
    {
        private int _counter;

        public string Generate() =>
            Convert
                .ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        BitConverter.GetBytes(Interlocked.Increment(ref _counter))
                    )
                )
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FailKeyRotationSaveInterceptor : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; } = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            Enabled
            && eventData
                .Context?.ChangeTracker.Entries<OverlayInstanceDomainEvent>()
                .Any(entry => entry.Entity.Kind == OverlayInstanceEventKind.KeyRotated) == true
                ? ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("Planned key-rotation commit failure.")
                )
                : ValueTask.FromResult(result);
    }
}
