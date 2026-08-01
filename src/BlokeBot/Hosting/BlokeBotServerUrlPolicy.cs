using Microsoft.AspNetCore.Hosting.Server.Features;

namespace BlokeBot.Hosting;

internal static class BlokeBotServerUrlPolicy
{
    internal const string DefaultHost = "127.0.0.1";
    internal const int DefaultPort = 8080;
    internal const string DefaultUrl = "http://127.0.0.1:8080";

    internal static string ExplicitUrl(string? host, int? port) =>
        $"http://{host ?? DefaultHost}:{port ?? DefaultPort}";

    internal static string LocalUrl(IServerAddressesFeature? addresses)
    {
        var address = addresses?.Addresses.FirstOrDefault() ?? DefaultUrl;
        return address
            .Replace("[::]", DefaultHost, StringComparison.Ordinal)
            .Replace("0.0.0.0", DefaultHost, StringComparison.Ordinal);
    }
}
