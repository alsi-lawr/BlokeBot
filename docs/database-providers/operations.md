# Main database operations

BlokeBot supports SQLite and PostgreSQL 17.x. SQLite remains the default provider.

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

## NixOS

Use a root-readable source file outside the Nix store. The module transfers it with a systemd credential.

```nix
services.blokebot = {
  enable = true;
  databaseProvider = "PostgreSql";
  postgresqlConnectionStringFile = "/run/secrets/blokebot-postgresql.connection";
};
```

The launch command uses `%d/blokebot-postgresql`. Systemd resolves `%d` to the credential directory for the service. The generated unit does not contain the connection string.

The launch wrapper clears database settings from `environmentFile`. It then applies the typed module settings before it starts BlokeBot.

## Docker Compose

The file `packaging/docker/compose.postgresql.yml` keeps the image default on SQLite. It adds an explicit PostgreSQL deployment example.

Create the protected files before startup:

1. Create `packaging/docker/secrets` with mode `0700`.
2. Create `packaging/docker/secrets/postgresql.password` with the database password and mode `0600`.
3. Create `packaging/docker/secrets/postgresql.connection` with the complete application connection string.
4. Use the same password in both protected files.
5. Set the connection-file owner to the image account with `sudo chown 1654:1654 packaging/docker/secrets/postgresql.connection`.
6. Set the connection-file mode with `sudo chmod 0400 packaging/docker/secrets/postgresql.connection`.
7. Add `SSL Mode=Require` for a remote server. Use certificate verification for production.
8. Run `docker compose -f packaging/docker/compose.postgresql.yml up --build`.

Do not commit either secret file. The Compose example mounts both files read-only. UID `1654` is the non-root `app` account in the runtime image.

## PostgreSQL ownership

The operator owns the PostgreSQL service. BlokeBot does not create a server, database, role, TLS certificate, backup, or replica.

Before deployment, complete these tasks:

1. Install a supported PostgreSQL 17.x release.
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

Test the restored database with one BlokeBot instance. Verify the admin page before public traffic reaches the service.

For a PostgreSQL upgrade, back up and test a restore first. Upgrade PostgreSQL before the BlokeBot application release.

Start one BlokeBot instance after the database upgrade. Let that instance apply application migrations.

The disaster-recovery plan must contain the database backup, matching state backup, connection secret, TLS trust, DNS, and one-instance deployment procedure.

## SQLite to PostgreSQL cutover

Stop BlokeBot before the cutover. Keep the SQLite file and state directory unchanged.

Run the offline transfer with a protected target connection file:

```text
blokebot database cutover-postgresql \
  --postgresql-connection-string-file /run/secrets/blokebot-postgresql.connection \
  --data-dir /var/lib/blokebot
```

The command verifies the PostgreSQL target. It does not change the active provider configuration.

After successful verification, change the provider configuration and restart BlokeBot. Verify the admin page before public traffic resumes.

Before the first PostgreSQL application write, you can retry the cutover or continue with the untouched SQLite deployment.

After the first PostgreSQL application write, do not return to SQLite. Repair or restore PostgreSQL and keep the PostgreSQL configuration.

BlokeBot does not provide a reverse transfer or a database downgrade.

## Release and deployment boundary

Repository tests use a disposable PostgreSQL service. They do not provision or switch a production database.

A release does not authorize a production cutover. Approve external provisioning and the provider switch as separate deployment work.
