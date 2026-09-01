# Main-database provider authorities

BlokeBot supports SQLite and PostgreSQL for the main database. Feature services still own their
transactions, timestamps, operation IDs, state transitions, audit rows, and public effects. The
persistence project owns only the provider-specific database mechanics listed here.

Plugin-private databases remain SQLite and are not part of these authorities.

## Transactions and admission

| Authority | SQLite | PostgreSQL | Invariant |
| --- | --- | --- | --- |
| `MainDatabaseWriteTransaction.StartImmediateAsync` | Opens a non-deferred transaction and acquires SQLite's write boundary. | Opens a Read Committed transaction and acquires the stable BlokeBot transaction-scoped advisory lock. | Only operations that already required SQLite immediate admission share one serial write boundary. A feature performs one read-modify-write decision at a time without adding a second transaction owner. |
| `StartImmediateWithBoundedAdmissionAsync` | Temporarily bounds the SQLite connection timeout while the write transaction starts. | Sets a transaction-local `lock_timeout` before it acquires the same advisory lock. | Admission cannot wait without a bound. Cancellation before admission writes nothing. |
| `MainDatabaseCommandTimeout.ApplyClaimBoundAsync` | Sets the EF command timeout and SQLite connection timeout. | Sets the EF/Npgsql command timeout and a transaction-local `lock_timeout`. | Claim lock contention has a bounded wait and caller cancellation remains distinct from a provider/query timeout. |
| `MainDatabaseStatements.LockHostAsync` | A no-op host update acquires the SQLite write boundary. | The same update acquires the PostgreSQL row lock. | Feature admission is ordered with the durable host feature transition. |

The advisory lock is not used by ordinary transactions, claims, or reads. It does not add a
multi-instance claim design. Transaction-collision retry loops remain bounded at their existing
owners. They retry only uniqueness or transaction-contention outcomes. They do not retry caller
cancellation, query timeout, transient connection failure, or terminal failure as if the commit
were known to have failed. Each retry reuses the logical timestamp and operation identity already
captured by the feature operation. Existing scheduler health policies can still classify an
operation timeout as transient without turning it into a transaction replay authority.

## SQL authorities

`MainDatabaseStatements` contains a closed set of named operations. It does not expose arbitrary
SQL or a generic repository.

| Operations | SQLite | PostgreSQL | Preserved behavior |
| --- | --- | --- | --- |
| automation run and delivery receipt admission | `INSERT OR IGNORE` | `INSERT ... ON CONFLICT DO NOTHING` | One durable run or receipt for each unique source occurrence. |
| community source receipt, custom-command claim, and automatic-raid claim | `INSERT OR IGNORE` | `INSERT ... ON CONFLICT DO NOTHING` | Duplicate delivery cannot apply the public effect twice. |
| raid-collaboration admission | guarded `INSERT OR IGNORE ... SELECT` | guarded `INSERT ... SELECT ... ON CONFLICT DO NOTHING` | The insert, feature flag, event fence, and duplicate check are one statement. |
| viewer stream session, attendance, and ambiguity tombstone | `INSERT OR IGNORE` | `INSERT ... ON CONFLICT DO NOTHING` | Repeated observation creates at most one authoritative row. |
| expired receipt and claim cleanup | quoted bounded SQL | the same quoted bounded SQL | Cleanup stays inside the caller's transaction and does not delete the current claim window. |
| plugin-owned automation flow selection | `json_valid` and `json_extract` | `jsonb` extraction | Plugin removal selects the same owned flows on both providers. |

The exact execution-site register is `main-database-raw-sql-v1.json`. Explicit SQLite migration
history, `HetznerBaselineBridge`, and the weekly-announcement migration interceptor remain
SQLite-only legacy code.

## Failure classification

| Provider outcome | Classification | Feature-level retry |
| --- | --- | --- |
| SQLite unique/primary-key constraint; PostgreSQL `23505` | `UniqueConflict` | Only an existing bounded collision loop can retry it. |
| PostgreSQL `40001`; EF optimistic concurrency | `SerializationFailure` | Yes, in an existing bounded transaction retry. |
| PostgreSQL `40P01` | `Deadlock` | Yes, in an existing bounded transaction retry. |
| SQLite busy/locked; PostgreSQL `55P03` | `LockTimeout` | Yes, in an existing bounded contention retry. |
| PostgreSQL `57014`, dependency timeout, or non-caller cancellation | `QueryTimeout` | No automatic transaction replay. |
| PostgreSQL `08xxx` or a transient `DbException` | `TransientConnection` | No automatic transaction replay because commit state can be ambiguous. |
| an `OperationCanceledException` with the caller token cancelled | `CallerCancellation` | Never. |
| all other errors | `Terminal` | Never. |

The classifier unwraps EF update exceptions but does not hide the original exception. Callers that
can return a typed contention result do so; other failures continue through their existing error
boundary.
