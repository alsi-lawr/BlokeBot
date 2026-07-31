using System.Net;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.Overlays;

public interface IOverlayDnsResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class SystemOverlayDnsResolver : IOverlayDnsResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken
    )
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}

public abstract record OverlayRemoteUrlDecision
{
    private OverlayRemoteUrlDecision() { }

    public sealed record Allowed : OverlayRemoteUrlDecision;

    public sealed record Rejected(string Message) : OverlayRemoteUrlDecision;
}

public sealed class OverlayRemoteUrlPolicy(
    IOverlayDnsResolver dns,
    IOptions<BlokeBotOptions> options
)
{
    public async Task<OverlayRemoteUrlDecision> ValidateAsync(
        Uri url,
        CancellationToken cancellationToken
    )
    {
        if (
            !url.IsAbsoluteUri
            || url.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Fragment)
        )
        {
            return Rejected("Remote layers require an absolute HTTPS URL without credentials.");
        }

        IReadOnlyList<IPAddress> addresses;
        if (IPAddress.TryParse(url.IdnHost, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await dns.ResolveAsync(url.IdnHost, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return Rejected("The remote layer host could not be resolved.");
            }
        }

        if (addresses.Count == 0)
        {
            return Rejected("The remote layer host could not be resolved.");
        }

        if (
            !options.Value.Overlays.Media.AllowPrivateNetworkTargets
            && addresses.Any(IsPrivateOrLocal)
        )
        {
            return Rejected(
                "Remote layers cannot target localhost, private, link-local, or unspecified network addresses."
            );
        }

        return new OverlayRemoteUrlDecision.Allowed();
    }

    internal static bool IsPrivateOrLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0
                || bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] >= 224
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                || (bytes[0] == 198 && bytes[1] is 18 or 19);
        }

        if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Loopback))
        {
            return true;
        }
        var ipv6 = address.GetAddressBytes();
        return (ipv6[0] & 0xfe) == 0xfc
            || (ipv6[0] == 0xfe && (ipv6[1] & 0xc0) == 0x80)
            || ipv6[0] == 0xff;
    }

    private static OverlayRemoteUrlDecision.Rejected Rejected(string message)
    {
        return new(message);
    }
}
