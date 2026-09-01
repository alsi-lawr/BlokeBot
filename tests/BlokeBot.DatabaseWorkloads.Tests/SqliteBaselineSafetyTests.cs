using Shouldly;

namespace BlokeBot.DatabaseWorkloads.Tests;

public sealed class SqliteBaselineSafetyTests
{
    [Test]
    public void ExistingDatabase_IsNeverOverwritten()
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = new byte[] { 1, 2, 3, 4 };
            File.WriteAllBytes(path, original);

            _ = Should.Throw<IOException>(() => SqliteBaselineSafety.RefuseExisting(path));

            File.ReadAllBytes(path).ShouldBe(original);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
