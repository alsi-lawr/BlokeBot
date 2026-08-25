using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed partial class PluginOutboundHttpClient
{
    private static bool ValidUri(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && uri.AbsoluteUri.Length <= PluginContractLimits.MaximumHttpUrlCharacters
        && uri.Scheme is "http" or "https"
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool ValidHeaders(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count > PluginContractLimits.MaximumHttpHeaders)
        {
            return false;
        }

        var bytes = 0;
        foreach (var header in headers)
        {
            if (
                !ValidHeaderName(header.Key)
                || header.Key.Equals("host", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("content-length", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase)
                || header.Value.Any(character => character is '\r' or '\n')
            )
            {
                return false;
            }
            bytes += System.Text.Encoding.UTF8.GetByteCount(header.Key);
            bytes += System.Text.Encoding.UTF8.GetByteCount(header.Value);
        }
        return bytes <= PluginContractLimits.MaximumHttpHeaderBytes;
    }

    private static bool ValidHeaderName(string name) =>
        name is { Length: > 0 and <= 128 }
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
