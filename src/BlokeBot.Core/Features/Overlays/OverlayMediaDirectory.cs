using System.Globalization;

namespace BlokeBot.Core.Features.Overlays;

public static class OverlayMediaDirectory
{
    public static string HostDirectory(string databasePath, int hostId)
    {
        var databaseDirectory =
            Path.GetDirectoryName(Path.GetFullPath(databasePath))
            ?? throw new InvalidOperationException("The database path has no parent directory.");
        return Path.Combine(
            databaseDirectory,
            "overlay-media",
            hostId.ToString(CultureInfo.InvariantCulture)
        );
    }
}
