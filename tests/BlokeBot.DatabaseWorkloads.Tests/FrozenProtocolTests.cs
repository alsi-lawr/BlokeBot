using Shouldly;

namespace BlokeBot.DatabaseWorkloads.Tests;

public sealed class FrozenProtocolTests
{
    [Test]
    public void MutatedProtocol_DoesNotPassFrozenDigest()
    {
        var root = FindRepositoryRoot();
        var protocol = Path.Combine(
            root,
            "tools",
            "BlokeBot.DatabaseWorkloads",
            "protocol",
            "blokebot-database-workloads-v1.json"
        );
        var digest = protocol + ".sha256";
        var temporaryProtocol = Path.GetTempFileName();
        try
        {
            File.Copy(protocol, temporaryProtocol, overwrite: true);
            FrozenProtocol
                .Load(temporaryProtocol, digest)
                .ProtocolId.ShouldBe("blokebot-database-workloads-v1");
            File.AppendAllText(temporaryProtocol, " ");

            _ = Should.Throw<ProtocolDriftException>(() =>
                FrozenProtocol.Load(temporaryProtocol, digest)
            );
        }
        finally
        {
            File.Delete(temporaryProtocol);
        }
    }

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
