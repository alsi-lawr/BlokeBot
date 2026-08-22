using System.Globalization;

namespace BlokeBot.Core.Features.Overlays;

public static class OverlayMediaDirectory
{
    public static string Root(string databasePath)
    {
        var databaseDirectory =
            Path.GetDirectoryName(Path.GetFullPath(databasePath))
            ?? throw new InvalidOperationException("The database path has no parent directory.");
        return Path.Combine(databaseDirectory, "overlay-media");
    }

    public static string DocumentDirectory(string databasePath) =>
        Path.Combine(Root(databasePath), "documents");

    public static string HostDirectory(string databasePath, int hostId) =>
        Path.Combine(Root(databasePath), hostId.ToString(CultureInfo.InvariantCulture));
}
