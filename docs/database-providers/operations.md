# Main database operations

BlokeBot supports SQLite and PostgreSQL 18.x. SQLite remains the default provider.

Install the current PostgreSQL 18 minor release. The PostgreSQL project recommends the current
minor release for each supported major version.

Configure one active BlokeBot instance for each main database. BlokeBot does not enforce this constraint with a process or database lease.

BlokeBot does not support multi-tenancy, high availability, or scale-out.

Plugin-private databases remain SQLite files in the state directory. A main database change does not move or convert them.

## Configuration

Use these environment settings:

| Setting | SQLite | PostgreSQL |
| --- | --- | --- |
| `BlokeBot__DatabaseProvider` | `Sqlite` or absent | `PostgreSql` |
| `BlokeBot__StateDirectory` | Local state directory | Local state directory |
| `BlokeBot__DatabasePath` | SQLite file path | Absent |
| `BlokeBot__PostgreSqlConnectionStringFile` | Absent | Protected connection-string file |

Do not put a PostgreSQL connection string in an environment value, command argument, Nix expression, image, or source file.

The connection file must contain `Host`, `Database`, and `Username`. Add credentials and TLS settings for the operator-managed server.

BlokeBot applies these PostgreSQL bounds:

| Connection setting | Default | Accepted range |
| --- | ---: | ---: |
| `Maximum Pool Size` | 20 | 1 through 50 |
| `Timeout` | 15 seconds | 1 through 30 seconds |
| `Command Timeout` | 30 seconds | 1 through 60 seconds |

BlokeBot rejects an explicit value outside these ranges. `Pooling=false` disables the pool for an offline database operation.

## NixOS installation

Use a root-readable source file outside the Nix store. The module transfers it with a systemd credential.

For a local PostgreSQL service, put this connection string in
`/etc/blokebot/postgresql.connection`:

```text
Host=/run/postgresql;Database=blokebot;Username=blokebot
```

Set the directory mode to `0700`. Set the file owner to `root` and the file mode to `0400`.

Update the NixOS package input first. Verify that `pkgs.postgresql_18` selects the current minor
release.

Add the PostgreSQL service, database, role, and BlokeBot settings:

```nix
services.postgresql = {
  enable = true;
  package = pkgs.postgresql_18;
  ensureDatabases = [ "blokebot" ];
  ensureUsers = [
    {
      name = "blokebot";
      ensureDBOwnership = true;
    }
  ];
};

services.blokebot = {
  enable = true;
  databaseProvider = "PostgreSql";
  postgresqlConnectionStringFile = "/etc/blokebot/postgresql.connection";
};

systemd.services.blokebot = {
  after = [ "postgresql.target" ];
  requires = [ "postgresql.target" ];
};
```

The launch command uses `%d/blokebot-postgresql`. Systemd resolves `%d` to the credential directory for the service. The generated unit does not contain the connection string.

The launch wrapper clears database settings from `environmentFile`. It then applies the typed module settings before it starts BlokeBot.

Apply the NixOS configuration. Then verify the service and database readiness:

```text
sudo nixos-rebuild switch
systemctl status blokebot
curl --fail http://127.0.0.1:8080/health/ready
```

## Docker Compose

The file `packaging/docker/compose.postgresql.yml` uses `postgres:18-alpine`. The major tag receives
the current PostgreSQL 18 minor release.

Use this procedure only for a new PostgreSQL 18 volume. Do not attach a PostgreSQL 17 data volume.

Create the protected files before startup:

1. Create `packaging/docker/secrets` with mode `0700`.
2. Create `packaging/docker/secrets/postgresql.password` with the database password and mode `0600`.
3. Create `packaging/docker/secrets/postgresql.connection` with the complete application connection string.
4. Use the same password in both protected files.
5. Set the connection-file owner to the image account with `sudo chown 1654:1654 packaging/docker/secrets/postgresql.connection`.
6. Set the connection-file mode with `sudo chmod 0400 packaging/docker/secrets/postgresql.connection`.
7. Use `Host=postgres`, `Database=blokebot`, `Username=blokebot`, and `SSL Mode=Disable` for this Compose network.
8. Run `docker compose -f packaging/docker/compose.postgresql.yml up --build --detach`.
9. Run `curl --fail --retry 30 --retry-all-errors --retry-delay 1 http://127.0.0.1:8080/health/ready`.

Use this value shape for the Compose connection file:

```text
Host=postgres;Port=5432;Database=blokebot;Username=blokebot;Password=<same-password>;SSL Mode=Disable
```

Do not commit either secret file. The Compose example mounts both files read-only. UID `1654` is the non-root `app` account in the runtime image.

PostgreSQL 18 stores its data below `/var/lib/postgresql/18/docker`. The Compose volume mounts the
PostgreSQL 18 parent data directory at `/var/lib/postgresql`.

## Native PostgreSQL installation

Install the current PostgreSQL 18 minor release from the operating-system package source. Start
the PostgreSQL service before BlokeBot.

Create the login role and database from an administrator session:

```text
sudo -u postgres createuser --login --pwprompt blokebot
sudo -u postgres createdb --owner=blokebot blokebot
```

Create `/etc/blokebot/postgresql.connection`. Give the file to the account that starts BlokeBot.
Set the file mode to `0400`.

The file must contain the database host, database, user name, password, and TLS settings. Use
`SSL Mode=VerifyFull` with the trusted root certificate for a remote production server.

Set these non-secret values in the native service manager:

```text
BlokeBot__DatabaseProvider=PostgreSql
BlokeBot__StateDirectory=/var/lib/blokebot
BlokeBot__PostgreSqlConnectionStringFile=/etc/blokebot/postgresql.connection
```

Start BlokeBot:

```text
blokebot serve --host 127.0.0.1 --port 8080 --data-dir /var/lib/blokebot
```

From another terminal, verify the process and database readiness:

```text
curl --fail http://127.0.0.1:8080/health/live
curl --fail http://127.0.0.1:8080/health/ready
```

Wait for `/health/ready` before public traffic reaches the service.

## Startup and migrations

BlokeBot checks the database connection and applies migrations before it starts the HTTP listener.

Startup uses this order:

1. BlokeBot checks the selected database connection.
2. BlokeBot applies the migration history for the selected provider.
3. BlokeBot starts HTTP and background services.
4. The readiness endpoint reports `ready`.

BlokeBot retries `provider-unavailable` failures five times. BlokeBot waits three seconds before each retry.

Only `provider-unavailable` causes a startup retry. BlokeBot treats all other startup categories as terminal.

The HTTP listener is absent during startup and migration. A connection refusal during this interval means that BlokeBot is not ready.

A terminal database failure stops BlokeBot with a nonzero exit status. The terminal startup message and its structured event use a redacted category.

## Health endpoints

`GET /health/live` confirms that the process listens. It does not access the database.

`GET /health/ready` checks database access and the migration history. The probe stops after two seconds.

A ready response contains the selected provider and `ready`. A failed response uses HTTP 503 and one redacted category:

- `provider-unavailable`
- `authentication-failure`
- `migration-failure`
- `pool-exhaustion`
- `command-timeout`
- `retryable-concurrency-conflict`
- `terminal-application-conflict`

The response does not contain host names, database names, user names, connection strings, SQL statements, or exception messages.

These endpoints exist only after the HTTP listener starts. They do not replace the operator constraint for one active instance.

## PostgreSQL ownership

The operator owns the PostgreSQL service. BlokeBot does not create a server, database, role, TLS certificate, backup, or replica.

Before deployment, complete these tasks:

1. Install the current PostgreSQL 18 minor release.
2. Configure trusted TLS and verify the server certificate.
3. Create one database for BlokeBot.
4. Create one login role for the active BlokeBot instance.
5. Give that role ownership of the BlokeBot database.
6. Restrict network access to the BlokeBot host.
7. Configure encrypted backups and retention.
8. Restore a backup into a disposable server.
9. Verify that BlokeBot starts against the restored database.

Do not give the application role superuser, replication, role-management, or database-creation privileges.

## Backup, restore, and upgrade

Stop BlokeBot or use a PostgreSQL-consistent online backup. Keep the state directory with the matching database backup.

The state backup must include tokens, data-protection keys, overlay media, schedules, plugin packages, and plugin-private SQLite files.

For a restore, restore PostgreSQL first. Restore the matching state directory before BlokeBot starts.

Test the restored database with one BlokeBot instance. Verify `/health/ready` before public traffic reaches the service.

For a PostgreSQL upgrade, back up and test a restore first. Upgrade PostgreSQL before the BlokeBot application release.

Start one BlokeBot instance after the database upgrade. Let that instance apply application migrations.

The disaster-recovery plan must contain the database backup, matching state backup, connection secret, TLS trust, DNS, and one-instance deployment procedure.

## SQLite to PostgreSQL cutover

Stop BlokeBot before the cutover. Keep the SQLite file and state directory unchanged.

Keep the active provider configuration on `Sqlite`. Prepare an empty target with the current
PostgreSQL v0.14 migration. Stop all other database sessions before the cutover.

The packaged cutover command does not create or migrate the PostgreSQL target schema.

From an administrator session for the target, grant only the extra function privilege that the
cutover requires:

```sql
GRANT EXECUTE ON FUNCTION pg_control_system() TO blokebot;
```

Run the offline transfer with a protected target connection file:

```text
blokebot database cutover-postgresql \
  --postgresql-connection-string-file /run/secrets/blokebot-postgresql.connection \
  --data-dir /var/lib/blokebot
```

The command verifies the PostgreSQL target. It does not change the active provider configuration.

After successful verification, revoke the temporary function privilege from an administrator
session for the target:

```sql
REVOKE EXECUTE ON FUNCTION pg_control_system() FROM blokebot;
```

After successful verification, change the provider configuration and restart BlokeBot. Verify `/health/ready` before public traffic resumes.

Before the first PostgreSQL application write, you can retry the cutover or continue with the untouched SQLite deployment.

After the first PostgreSQL application write, do not return to SQLite. Repair or restore PostgreSQL and keep the PostgreSQL configuration.

BlokeBot does not provide a reverse transfer or a database downgrade.

## Release and deployment boundary

Repository tests use a disposable PostgreSQL service. They do not provision or switch a production database.

A release does not authorize a production cutover. Approve external provisioning and the provider switch as separate deployment work.
