using System.Reflection;
using System.Runtime.CompilerServices;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class NamingConventionTests
{
    [Test]
    public void OwnedTypeDeclarations_Inspecting_HaveNoRedundantVendorOrClientNames()
    {
        var declarations = OwnedAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Select(type => type.Name)
            .Where(HasRedundantName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        declarations.ShouldBeEmpty();
    }

    [Test]
    public void IncludedWorkingTreeSourceFiles_Inspecting_HaveNoRedundantVendorOrClientNames()
    {
        var repositoryRoot = FindRepositoryRoot();
        var declarations = new[] { "src", "tests" }
            .SelectMany(path =>
                Directory.EnumerateFiles(
                    Path.Combine(repositoryRoot, path),
                    "*.cs",
                    SearchOption.AllDirectories
                )
            )
            .Where(path => IsIncludedWorkingTreeSourcePath(repositoryRoot, path))
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .Where(HasRedundantName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        declarations.ShouldBeEmpty();
    }

    private static Assembly[] OwnedAssemblies()
    {
        return
        [
            typeof(ChatMessage).Assembly,
            typeof(BlokeBot.Eventing.EventSubscriptionSet).Assembly,
            typeof(BlokeBot.Functional.Option<>).Assembly,
            typeof(BlokeBot.Persistence.BlokeBotDbContext).Assembly,
            typeof(HelixClient).Assembly,
            typeof(BotIdentity).Assembly,
            typeof(BotSettings).Assembly,
            typeof(Features.Points.Balances.HelixPointTargetUserLookup).Assembly,
        ];
    }

    private static bool HasRedundantName(string name)
    {
        return name.Contains("Twitch", StringComparison.Ordinal)
            || name.EndsWith("ApiClient", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlokeBot.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root could not be located.");
    }

    private static bool IsIncludedWorkingTreeSourcePath(string repositoryRoot, string path)
    {
        var segments = Path.GetRelativePath(repositoryRoot, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(segment =>
            segment is "bin" or "obj" or "node_modules" or ".agent-workspace"
        );
    }
}
