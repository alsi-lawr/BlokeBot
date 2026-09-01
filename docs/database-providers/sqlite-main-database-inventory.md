# Main-database SQLite inventory

This inventory is frozen at source commit `6e8af8ef6fc75df47bac7b15a283c851b2bfdc07`. It covers the
BlokeBot main database. Per-plugin private databases in `BlokeBot.Plugins.Features` are separate,
plugin-owned SQLite stores and are not part of the database-provider change.

## Packages and provider construction

| Classification | Source | SQLite dependency |
| --- | --- | --- |
| package | `src/BlokeBot.Persistence/BlokeBot.Persistence.csproj` | `Microsoft.EntityFrameworkCore.Sqlite` and `SQLitePCLRaw.bundle_e_sqlite3` |
| operational | `src/BlokeBot.Persistence/BlokeBotPersistenceServiceCollectionExtensions.cs` | builds one file connection string and always calls `UseSqlite` |
| migration-only | `src/BlokeBot.Persistence/BlokeBotDbContextFactory.cs` | design-time `UseSqlite("Data Source=blokebot.db")` |
| EF-neutral | `src/BlokeBot.Persistence/BlokeBotDatabaseInitializer.cs` | runs the legacy bridge and then EF migrations |
| legacy bridge | `src/BlokeBot.Persistence/HetznerBaselineBridge.cs` | casts to `SqliteConnection`, reads `sqlite_master`, and creates SQLite migration-history rows |
| migration-only | `src/BlokeBot.Persistence/WeeklyAnnouncementMigrationInterceptor.cs` | branches on `SqliteConnection` while upgrading legacy announcements |
| EF-neutral | `src/BlokeBot.Persistence/Migrations/` | the released SQLite migration history and model snapshot; retained as a SQLite history |

The migration files with authored SQL are:

- `20260722220325_RouteCustomCommandRepliesByArgumentCount.cs`
- `20260728201821_v0.3.0_NativeTwitchFeatureSwitch.cs`
- `20260730054804_v0.4.0_MomentConvergence.cs`
- `20260730084046_v0.5.0_OverlayInstances.cs`
- `20260730141846_v0.5.0_OverlayFeatureSwitch.cs`
- `20260730162013_v0.5.0_ViewerCommandCatalog.cs`
- `20260730202307_v0.5.0_IndependentChatTools.cs`
- `20260731043353_v0.6.0_OverlayCues.cs`
- `20260731141254_v0.6.0_OverlayAppearance.cs`
- `20260804000549_v0.7.0_CustomCommandSelectedUserAccess.cs`
- `20260804084816_v0.7.0_CustomCommandAutomationRuntime.cs`
- `20260810154030_v0.9.0_BingoOpaqueAssignments.cs`
- `20260811051820_v0.10.0_ViewerPassportLoginHistory.cs`
- `20260811062237_v0.10.0_ViewerPassportAmbiguousLogins.cs`
- `20260822142039_v0.12.0.cs`
- `20260826174307_v0.13.0.cs`

All other files in that directory use EF migration operations but remain part of the SQLite-only
released history.

## Dialect, transactions, and error classification

| Classification | Sources | Dependency |
| --- | --- | --- |
| SQL dialect-specific | `AutomationRuntimeService.cs`, `TwitchEventAutomationRuntime.cs`, `CustomCommandInvocationClaimStore.cs`, `CommunityProgressionService.cs`, `RaidCollaborationService.cs`, `AutomaticRaidShoutoutRunner.cs`, `ViewerPassportService.cs`, `ViewerPassportAmbiguityTombstones.cs` | `INSERT OR IGNORE` admission, receipt, and idempotency writes |
| SQL dialect-specific | `EfPluginFeatureStore.cs` | `FromSqlInterpolated` with SQLite JSON predicates over automation provenance |
| JSON/check-specific | `BlokeBotDbContext.Overlays.Cues.cs`, `BlokeBotDbContext.Overlays.Instances.cs`, `BlokeBotDbContext.PluginFeatures.cs` | `json_valid`, `json_type`, `json_extract`, blob-length checks, and SQLite identifier syntax |
| transaction/locking-specific | `BlokeRaidService.cs`, `BountyService.cs`, `CollectiveService.cs`, `MomentHubService.cs`, `AutomaticRaidOutcomeImmediateTransaction.cs` | casts the EF connection and starts a non-deferred SQLite transaction |
| transaction/locking-specific | `AutomaticRaidShoutoutRunner.cs`, `BountyService.cs` | changes `SqliteConnection.DefaultTimeout` around write admission; the automatic-raid path also sets EF command timeout to the same one-second bound |
| transaction/locking-specific | `BingoService.cs`, `BlokeRaidService.cs`, `BountyService.cs`, `CommunityProgressionService.cs`, `MomentAttachmentService.cs`, `MomentHubService.cs`, `RequestBoardService.cs`, `ClipMarkerService.cs` | recognizes busy/locked and, where applicable, SQLite unique-constraint codes |
| transaction/locking-specific | `ConfigurationActivationWorker.cs`, `EfPublicChatOutbox.Transitions.cs`, `AutomaticRaidShoutoutOutcomeAuthority.cs`, `PointsGiveawaySchedulerHealth.cs`, `ViewerPrivacy.Transactions.cs` | recognizes SQLite busy/locked contention; outbox also recognizes SQLite uniqueness |
| transaction/locking-specific | `EfPluginFeatureStore.Configuration.cs` | recognizes SQLite constraint error 19 as a configuration conflict |

The paths above are relative to `src/BlokeBot.Core/Features`, except persistence paths, which are
relative to `src/BlokeBot.Persistence`. EF `BeginTransactionAsync`, `ExecuteUpdateAsync`, LINQ, and
normal `SaveChangesAsync` calls not listed here are EF-neutral at the API surface, but their
isolation, locking, query translation, and exception behavior still require the workload and
transaction comparison in BLOKEBOT-271.

## State paths, CLI, privacy, and deployment

| Classification | Source | Coupling |
| --- | --- | --- |
| state-path/CLI | `src/BlokeBot/Hosting/BlokeBotStatePaths.cs` | `blokebot.db` selects the state directory and co-locates the token cache and data-protection keys |
| state-path/CLI | `src/BlokeBot/Hosting/BlokeBotHost.cs` | maps `--data-dir`/configuration to `BlokeBot:DatabasePath`, persistence, and data protection |
| state-path/CLI | `src/BlokeBot/Cli/BlokeBotCli.cs` | documents `--data-dir` as the home of `blokebot.db` and token state |
| state-path/CLI | `src/BlokeBot.Core/Hosting/BlokeBotApplication.cs`, `src/BlokeBot.Core/BlokeBotOptions.cs` | requires and exposes a database file path |
| state-path/CLI | `src/BlokeBot/Hosting/BlokeBotPrivacyActions.cs` | privacy export/erase opens the SQLite file directly and refuses a missing file |
| local-state consumer | `OverlayMediaDirectory.cs`, `OverlayCueService.MediaStorage.Helpers.cs`, `OverlayMediaMaintenanceService.cs`, `BotHostRemovalService.cs` | derive overlay media directories from the database directory |
| local-state consumer | `PluginScheduleFileStore.cs` | derives `plugin-schedules.json` from the database directory |
| simulation | `src/BlokeBot.Simulation/SimulationDatabaseKeeper.cs`, `SimulationApplication.cs` | uses an in-memory shared SQLite database; intentionally retained |
| packaging | `src/BlokeBot/BlokeBot.csproj` | excludes `*.db`, `*.db-wal`, and `*.db-shm` development artifacts |
| deployment | `flake.nix` | creates `/data`, runs there, and sets only `BlokeBot__DatabasePath=/data/blokebot.db` |
| documentation | `README.md` | the container example persists `/data` as one local volume |

The local state directory also contains marketplace packages and plugin-owned private database
files through their existing stores. Changing the main provider must not move, import, or open
those plugin-private databases.

## Reproduce the scan

Run these searches at the inventory commit and review every match. Generated migration designers
and tests are excluded from the direct-runtime searches, and `BlokeBot.Plugins.Features` is
excluded because it owns plugin-private SQLite.

```sh
rg -n --glob '*.cs' \
  --glob '!src/BlokeBot.Plugins.Features/**' \
  --glob '!src/BlokeBot.Persistence/Migrations/**' \
  'Sqlite(Connection|Exception)|SQLitePCL|UseSqlite' src

rg -n --glob '*.cs' \
  --glob '!src/BlokeBot.Plugins.Features/**' \
  --glob '!src/BlokeBot.Persistence/Migrations/**' \
  'ExecuteSql|FromSql|INSERT OR IGNORE|json_(valid|type|extract)|BeginTransaction\(deferred|PRAGMA|DefaultTimeout' src

rg -n 'DatabasePath|StateDirectory|blokebot\.db|AddBlokeBotPersistence' \
  src/BlokeBot src/BlokeBot.Core src/BlokeBot.Persistence src/BlokeBot.Simulation flake.nix README.md
```
