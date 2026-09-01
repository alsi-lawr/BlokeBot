# PostgreSQL capability contract

PostgreSQL is the selected first server provider. BLOKEBOT-270 supplies provider configuration and
schema history. BLOKEBOT-271 supplies the SQL, transaction, failure, and workload authorities. The
measured provider comparison is in
`evidence/sqlite-postgresql-comparison-20260901.md`. BLOKEBOT-272 and BLOKEBOT-273 still own
cutover, packaging, deployment configuration, CI, and operator procedures.

## EF provider and schema

- Use an Npgsql EF Core provider compatible with the repository's EF Core version. Provider
  selection must remain one runtime configuration authority; SQLite remains the default.
- Maintain a PostgreSQL baseline and forward migration history separate from the released SQLite
  history. Do not replay `HetznerBaselineBridge`, SQLite PRAGMAs, SQLite JSON checks, or SQLite
  migration-history repair against PostgreSQL.
- Map every current key, unique constraint, filtered index, check, enum token, JSON validity rule,
  concurrency token, generated identifier, UTC timestamp, numeric points value, and maximum length
  deliberately. Schema creation and upgrade must be transactional where PostgreSQL permits it.
- Preserve one logical `BlokeBotDbContext` model and provider-neutral application state paths.
  PostgreSQL configuration must not relocate token cache, data-protection keys, overlay media,
  marketplace packages, plugin schedules, or plugin-private SQLite files.

## Transactions, claims, and failures

- Preserve the existing feature service as transaction owner. Do not introduce a generic
  repository or a second unit of work.
- Define an isolation level for each authority. Ordinary consistent writes may use Read Committed;
  revision checks, immediate-write authorities, activations, claims, and other serial schedules
  must use an explicit lock or stronger isolation where their accepted invariant requires it.
- Queue/outbox claims must select and update atomically. `FOR UPDATE`, `SKIP LOCKED`, advisory locks,
  or `ON CONFLICT` are allowed only inside the provider authority whose invariant requires them.
- Classify unique violation, serialization failure, deadlock, lock timeout, statement timeout,
  transient connection failure, caller cancellation, and terminal application failure separately.
  Retries must be bounded and preserve the logical operation ID and timestamp.
- The frozen `blokebot-database-workloads-v1` seed, operation order, barrier rounds, cancellation
  points, invariants, and result schema are immutable inputs to BLOKEBOT-271. Provider timing and
  wait measurements are variable outputs and must be reported separately.

## Operations and topology

- Support exactly one active BlokeBot application instance per main database. Multi-tenancy,
  multi-writer scale-out, HA application claims, and database downgrade are outside v0.14.
- Accept the connection string through a protected secret input. Never place credentials in CLI
  arguments, generated units, logs, result JSON, exports, or public pages.
- Bound connection-pool size, command timeout, lock timeout, startup retry, and migration ownership.
  Readiness must remain false while the provider is unavailable or migrations have failed.
- Expose redacted provider health, pool use, query duration, timeout, retry, deadlock, serialization,
  and lock-wait diagnostics. PostgreSQL-native evidence may use `pg_stat_activity`, lock views, and
  `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)`; `pg_stat_statements` is optional and must be declared if
  required.
- Provisioning, supported PostgreSQL versions, roles, TLS, database creation, storage, backup,
  restore, server upgrade, monitoring, and disaster recovery are operator-owned prerequisites.
  Validate logical and physical restore before cutover. Application startup must not create or
  administer a PostgreSQL server.
- No production database switch, credential creation, external provisioning, release, or deployment
  is authorized by this baseline.
