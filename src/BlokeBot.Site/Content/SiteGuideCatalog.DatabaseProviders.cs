namespace BlokeBot.Site.Content;

internal static partial class SiteGuideCatalog
{
    private static IEnumerable<SiteGuidePage> CreateDatabaseProviderPages()
    {
        yield return new SiteGuidePage
        {
            Route = "/server-owners/database",
            Eyebrow = "Server owners",
            Title = "Choose and operate the main database",
            Summary =
                "SQLite is the default. PostgreSQL 17.x is available for one active BlokeBot instance.",
            Sections =
            [
                new SiteGuideSection
                {
                    Heading = "Provider configuration",
                    Bullets =
                    [
                        "Use Sqlite for the default local database file.",
                        "Use PostgreSql with BlokeBot__PostgreSqlConnectionStringFile for PostgreSQL 17.x.",
                        "Keep BlokeBot__StateDirectory on persistent storage for both providers.",
                        "Configure one active BlokeBot instance for each main database.",
                    ],
                    Note =
                        "Do not put the PostgreSQL connection string in an environment value, command argument, image or source file.",
                },
                new SiteGuideSection
                {
                    Heading = "Startup and health",
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
                    Heading = "PostgreSQL responsibilities",
                    Bullets =
                    [
                        "Provision the PostgreSQL server, database and application role.",
                        "Configure certificate-verified TLS and restrict network access.",
                        "Back up PostgreSQL and the matching BlokeBot state directory.",
                        "Test a restore before a cutover or PostgreSQL upgrade.",
                        "Keep one active BlokeBot instance during migrations and normal operation.",
                    ],
                    Note =
                        "BlokeBot does not enforce the one-instance constraint with a process or database lease. It does not provide high availability, scale-out or multi-tenancy.",
                },
                new SiteGuideSection
                {
                    Heading = "Cutover recovery",
                    Paragraphs =
                    [
                        "Stop BlokeBot before the offline SQLite-to-PostgreSQL cutover. The command verifies the target but does not change active configuration.",
                    ],
                    Bullets =
                    [
                        "Before the first PostgreSQL application write, retry the cutover or continue with untouched SQLite.",
                        "After the first PostgreSQL application write, repair or restore PostgreSQL. Do not return to SQLite.",
                    ],
                    Links =
                    [
                        new SiteLink(
                            "Main database operations",
                            "https://github.com/alsi-lawr/BlokeBot/blob/master/docs/database-providers/operations.md"
                        ),
                    ],
                },
            ],
            Next = [new SiteLink("Server owner setup", "server-owners")],
        };
    }
}
