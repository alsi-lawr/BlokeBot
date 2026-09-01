using System.Globalization;

namespace BlokeBot.Core.Features.Overlays;

public static class OverlayMediaDirectory
{
    public static string Root(string stateDirectory) =>
        Path.Combine(Path.GetFullPath(stateDirectory), "overlay-media");

    public static string DocumentDirectory(string stateDirectory) =>
        Path.Combine(Root(stateDirectory), "documents");

    public static string HostDirectory(string stateDirectory, int hostId) =>
        Path.Combine(Root(stateDirectory), hostId.ToString(CultureInfo.InvariantCulture));
}
