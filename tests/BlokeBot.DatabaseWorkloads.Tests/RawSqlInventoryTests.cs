using Shouldly;

namespace BlokeBot.DatabaseWorkloads.Tests;

public sealed class RawSqlInventoryTests
{
    [Test]
    public void CanonicalInventory_CoversEveryMainDatabaseRawSqlStatement()
    {
        var root = FindRepositoryRoot();
        var inventory = Load(root);

        Should.NotThrow(() => RawSqlInventory.Verify(root, inventory));
    }

    [Test]
    public void InventoryWithMissingStatement_IsRejected()
    {
        var root = FindRepositoryRoot();
        var inventory = Load(root);
        var incomplete = inventory with { Statements = inventory.Statements.Skip(1).ToArray() };

        _ = Should.Throw<InventoryDriftException>(() => RawSqlInventory.Verify(root, incomplete));
    }

    private static RawSqlInventoryDocument Load(string root) =>
        RawSqlInventory.Load(
            Path.Combine(root, "docs", "database-providers", "main-database-raw-sql-v1.json")
        );

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "BlokeBot.slnx"))
        )
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
