namespace BlokeBot.Site.Tests;

internal static class SiteTestPaths
{
    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

    internal static string SiteRoot { get; } = Path.Combine(RepositoryRoot, "src", "BlokeBot.Site");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlokeBot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the BlokeBot repository root.");
    }
}
