using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed partial class PluginOutboundHttpClient
{
    private static async ValueTask<ReadOnlyMemory<byte>?> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken
    )
    {
        if (content.Headers.ContentLength is > PluginContractLimits.MaximumHttpResponseBodyBytes)
        {
            return null;
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }
            if (output.Length + read > PluginContractLimits.MaximumHttpResponseBodyBytes)
            {
                return null;
            }
            output.Write(buffer, 0, read);
        }
    }

    private static ImmutableDictionary<string, string> ResponseHeaders(HttpResponseMessage response)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        var bytes = 0;
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (builder.Count >= PluginContractLimits.MaximumHttpHeaders)
            {
                break;
            }
            var value = string.Join(",", header.Value);
            var next =
                System.Text.Encoding.UTF8.GetByteCount(header.Key)
                + System.Text.Encoding.UTF8.GetByteCount(value);
            if (bytes + next > PluginContractLimits.MaximumHttpHeaderBytes)
            {
                break;
            }
            builder[header.Key] = value;
            bytes += next;
        }
        return builder.ToImmutable();
    }
}
