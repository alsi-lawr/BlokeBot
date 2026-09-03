# Main-database provider inventory

This inventory was reconciled through BLOKEBOT-272 from commit
`57a60989da25bda9464025bfda7272a2d3dbdf90`. SQLite remains the default main provider. PostgreSQL
is the first server provider. Plugin-private databases in `BlokeBot.Plugins.Features` remain
separate SQLite stores.

## Runtime provider boundaries

| Area | Authority |
| --- | --- |
| provider selection, connection secret, migrations, and model | `BlokeBotDatabaseConfiguration`, `BlokeBotDatabaseProviderExtensions`, and the separate PostgreSQL migration assembly |
| immediate writes and bounded admission | `MainDatabaseWriteTransaction` and `MainDatabaseCommandTimeout` |
| uniqueness, serialization, deadlock, lock/query timeout, connection, cancellation, and terminal errors | `MainDatabaseFailureClassifier` |
| host admission, idempotent inserts, claims, cleanup, and provider JSON | the named methods in `MainDatabaseStatements*` |
| released SQLite migration and legacy bridge | `BlokeBot.Persistence/Migrations`, `HetznerBaselineBridge`, and `WeeklyAnnouncementMigrationInterceptor` |
| frozen provider comparison | `BlokeBot.DatabaseWorkloads` with the unchanged `blokebot-database-workloads-v1` document |

Feature and scheduler code has no direct main-database `SqliteConnection`, `SqliteTransaction`,
`SqliteException`, `INSERT OR IGNORE`, SQLite JSON function, or raw SQL execution. The allowed
SQLite symbols outside generated migrations are now confined to provider configuration,
persistence authorities, explicit SQLite legacy code, simulations/tests, and the SQLite workload
adapter.

## Raw SQL register

`main-database-raw-sql-v1.json` registers 50 execution sites:

- four SQLite-only `HetznerBaselineBridge` catalog/history statements;
- nine named SQLite/PostgreSQL idempotent insert authorities;
- host locking, two bounded cleanup statements, and automatic-raid cleanup;
- separate SQLite and PostgreSQL plugin JSON queries;
- the two closed provider SQL dispatch sites;
- PostgreSQL's two transaction-local lock bounds and transaction-scoped immediate-write lock; and
- 26 offline cutover statements: seven target preparation statements; six identity, ownership,
  session, and physical catalog reads; two SQLite exclusive-lease statements; three canonical
  copy statements; two bounded self-reference statements; and six sequence or constraint
  verification statements.

The register records the exact path, line, API, source marker, purpose, and dialect dependency. The
verifier also requires the exact reviewed set of four `sqlite_schema` references: three in the
legacy bridge and one cutover physical-catalog read. It fails for an unregistered raw SQL API, an
unregistered named insert authority, a stale marker, an unused entry, an unreviewed catalog read,
or any `sqlite_master` reference.

## Reproduce the inventory

```sh
dotnet run -c Release --project tools/BlokeBot.DatabaseWorkloads -- verify-inventory \
  --repo-root . \
  --inventory docs/database-providers/main-database-raw-sql-v1.json

rg -n --glob '*.cs' \
  --glob '!src/BlokeBot.Plugins.Features/**' \
  --glob '!src/BlokeBot.Persistence/Migrations/**' \
  'Sqlite(Connection|Transaction|Exception)|INSERT OR IGNORE|json_(valid|type|extract)|DefaultTimeout' \
  src/BlokeBot.Core src/BlokeBot.Persistence
```

The second command must show provider-specific results only in cohesive persistence authorities or
explicit SQLite legacy code. It must not show a feature or scheduler dependency.
