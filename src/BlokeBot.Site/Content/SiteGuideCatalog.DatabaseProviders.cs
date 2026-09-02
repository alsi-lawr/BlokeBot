namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateDatabaseProviderPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/server-owners/database",
            Eyebrow = "Server owners",
            Title = "Install and operate the main database",
            Summary =
                "SQLite is the default. BlokeBot supports the current PostgreSQL 18 minor release for one active instance per main database.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Provider configuration",
                    Bullets =
                    [
                        "Use Sqlite for the default local database file.",
                        "Use PostgreSql with BlokeBot__PostgreSqlConnectionStringFile for PostgreSQL 18.x.",
                        "Install the current PostgreSQL 18 minor release.",
                        "Keep BlokeBot__StateDirectory on persistent storage for both providers.",
                        "Configure one active BlokeBot instance for each main database.",
                    ],
                    Note =
                        "Do not put the PostgreSQL connection string in an environment value, command argument, image, or source file.",
                },
                new SiteGuideSection
                {
                    Heading = "Docker Compose secrets",
                    Paragraphs =
                    [
                        "The Compose file uses postgres:18-alpine and starts BlokeBot. It stores each service state in a named volume.",
                    ],
                    Steps =
                    [
                        "Open the BlokeBot repository root.",
                        "Create the protected secret directory and files with the commands below.",
                        "Write only the database password to postgresql.password.",
                        "Write the complete BlokeBot connection string to postgresql.connection.",
                        "Use Host=postgres;Port=5432;Database=blokebot;Username=blokebot;Password=<same-password>;SSL Mode=Disable.",
                    ],
                    Code = """
                        umask 077
                        mkdir -p packaging/docker/secrets
                        ${EDITOR:-vi} packaging/docker/secrets/postgresql.password
                        ${EDITOR:-vi} packaging/docker/secrets/postgresql.connection
                        chmod 0600 packaging/docker/secrets/postgresql.password
                        sudo chown 1654:1654 packaging/docker/secrets/postgresql.connection
                        sudo chmod 0400 packaging/docker/secrets/postgresql.connection
                        """,
                    Note =
                        "Do not commit these files. UID 1654 is the non-root account in the BlokeBot image.",
                },
                new SiteGuideSection
                {
                    Heading = "Docker Compose startup",
                    Steps =
                    [
                        "Use a new PostgreSQL 18 volume.",
                        "Start both services with the Compose file.",
                        "Wait for the readiness request to succeed.",
                    ],
                    Code = """
                        docker compose -f packaging/docker/compose.postgresql.yml up --build --detach
                        curl --fail --retry 30 --retry-all-errors --retry-delay 1 http://127.0.0.1:8080/health/ready
                        """,
                    Note =
                        "Do not attach a PostgreSQL 17 data volume. PostgreSQL 18 uses /var/lib/postgresql as the volume mount.",
                },
                new SiteGuideSection
                {
                    Heading = "NixOS protected credential",
                    Steps =
                    [
                        "Create /etc/blokebot with mode 0700.",
                        "Create /etc/blokebot/postgresql.connection with owner root and mode 0400.",
                        "Write the local socket connection string below to the file.",
                        "Keep the source file outside the Nix store.",
                    ],
                    Code = "Host=/run/postgresql;Database=blokebot;Username=blokebot",
                    Note =
                        "The BlokeBot module transfers this file with a systemd credential. The generated unit does not contain the connection string.",
                },
                new SiteGuideSection
                {
                    Heading = "NixOS PostgreSQL configuration",
                    Steps =
                    [
                        "Update the NixOS package input to the current PostgreSQL 18 minor release.",
                        "Add the PostgreSQL 18 service and BlokeBot settings.",
                        "Make the local BlokeBot service depend on PostgreSQL.",
                        "Apply the NixOS configuration.",
                    ],
                    Code = """
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
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "NixOS startup and health",
                    Steps =
                    [
                        "Apply the new system configuration.",
                        "Verify that the BlokeBot service stays active.",
                        "Verify the database readiness endpoint.",
                    ],
                    Code = """
                        sudo nixos-rebuild switch
                        systemctl status blokebot
                        curl --fail http://127.0.0.1:8080/health/ready
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Native PostgreSQL installation",
                    Steps =
                    [
                        "Install the current PostgreSQL 18 minor release from the operating-system package source.",
                        "Start the PostgreSQL service.",
                        "Create the BlokeBot login role with a password prompt.",
                        "Create the BlokeBot database with that role as owner.",
                    ],
                    Code = """
                        sudo -u postgres createuser --login --pwprompt blokebot
                        sudo -u postgres createdb --owner=blokebot blokebot
                        """,
                    Note =
                        "Do not give the role superuser, replication, role-management, or database-creation privileges.",
                },
                new SiteGuideSection
                {
                    Heading = "Native BlokeBot configuration",
                    Steps =
                    [
                        "Create /etc/blokebot/postgresql.connection for the BlokeBot service account.",
                        "Set the connection-file mode to 0400.",
                        "Add the database host, database, user name, password, and TLS settings.",
                        "Set the non-secret values below in the service manager.",
                        "Start one BlokeBot process with the command below.",
                    ],
                    Code = """
                        export BlokeBot__DatabaseProvider=PostgreSql
                        export BlokeBot__StateDirectory=/var/lib/blokebot
                        export BlokeBot__PostgreSqlConnectionStringFile=/etc/blokebot/postgresql.connection

                        blokebot serve --host 127.0.0.1 --port 8080 --data-dir /var/lib/blokebot
                        """,
                    Note =
                        "Use SSL Mode=VerifyFull and a trusted root certificate for a remote production server.",
                },
                new SiteGuideSection
                {
                    Heading = "Native startup health",
                    Steps =
                    [
                        "Open another terminal after BlokeBot starts.",
                        "Verify both health endpoints before public traffic starts.",
                    ],
                    Code = """
                        curl --fail http://127.0.0.1:8080/health/live
                        curl --fail http://127.0.0.1:8080/health/ready
                        """,
                },
                new SiteGuideSection
                {
                    Heading = "Startup and health behavior",
                    Paragraphs =
                    [
                        "BlokeBot checks the database and applies migrations before it starts the HTTP listener. A connection refusal means that BlokeBot is not ready.",
                    ],
                    Bullets =
                    [
                        "BlokeBot retries provider unavailability five times. Each retry waits three seconds.",
                        "/health/live confirms that the process listens. It does not access the database.",
                        "/health/ready checks database access and the migration history within two seconds.",
                        "A terminal startup failure stops BlokeBot with a redacted category and a nonzero exit status.",
                    ],
                    Note =
                        "The health endpoints do not enforce the operator constraint for one active instance.",
                },
                new SiteGuideSection
                {
                    Heading = "SQLite cutover preconditions",
                    Steps =
                    [
                        "Stop the SQLite BlokeBot instance.",
                        "Back up the SQLite file and the matching state directory.",
                        "Keep the active provider configuration on Sqlite.",
                        "Start PostgreSQL 18 and create the application login.",
                        "Do not create the application database.",
                        "Create a protected administrator connection file for an existing maintenance database.",
                        "Create a protected application connection file for the new database and the application login.",
                    ],
                    Note =
                        "The administrator login must be a superuser, or it must have CREATEDB, EXECUTE on pg_control_system(), and membership of the application login.",
                },
                new SiteGuideSection
                {
                    Heading = "SQLite cutover command",
                    Steps =
                    [
                        "Run the offline transfer with both protected connection files.",
                        "Rerun the same command to resume an interrupted transfer.",
                        "Reuse the operation ID if you set --operation-id.",
                        "Change the provider to PostgreSql only after successful verification.",
                        "Start one BlokeBot instance and verify /health/ready.",
                    ],
                    Code = """
                        blokebot database cutover-postgresql \
                          --postgresql-administrator-connection-string-file /etc/blokebot/postgresql-admin.connection \
                          --postgresql-application-connection-string-file /etc/blokebot/postgresql.connection \
                          --data-dir /var/lib/blokebot
                        """,
                    Note =
                        "The command migrates SQLite first. It then creates the database, applies the PostgreSQL schema, and copies and verifies the data. It rejects a database that exists without a matching receipt. It does not drop a database or change the active provider configuration.",
                },
                new SiteGuideSection
                {
                    Heading = "Cutover recovery boundary",
                    Bullets =
                    [
                        "Before the first PostgreSQL application write, retry the cutover or continue with untouched SQLite.",
                        "After the first PostgreSQL application write, repair or restore PostgreSQL.",
                        "Do not return to SQLite after the first PostgreSQL application write.",
                        "BlokeBot does not provide a reverse transfer or database downgrade.",
                    ],
                },
                new SiteGuideSection
                {
                    Heading = "PostgreSQL responsibilities",
                    Bullets =
                    [
                        "Configure certificate-verified TLS and restrict network access.",
                        "Back up PostgreSQL and the matching BlokeBot state directory.",
                        "Test a restore before a cutover or PostgreSQL upgrade.",
                        "Keep one active BlokeBot instance during migrations and normal operation.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "Main database operations",
                            "https://github.com/alsi-lawr/BlokeBot/blob/master/docs/database-providers/operations.md"
                        ),
                        new SiteLink(
                            "PostgreSQL version policy",
                            "https://www.postgresql.org/support/versioning/"
                        ),
                    ],
                    Note =
                        "BlokeBot does not provide high availability, scale-out, or multi-tenancy.",
                },
            ],
            Next = [new SiteLink("Server owner setup", "server-owners")],
        };
    }
}
