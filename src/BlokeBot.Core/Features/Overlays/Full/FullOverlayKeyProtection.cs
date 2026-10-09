using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed class FullOverlayKeyProtection(IDataProtectionProvider protection)
{
    internal string Protect(int hostId, Guid overlayId, string key) =>
        Protector(hostId, overlayId).Protect(key);

    internal FullOverlayPrivateAccess? Read(int hostId, Guid overlayId, string? protectedKey)
    {
        if (protectedKey is null)
        {
            return null;
        }
        try
        {
            var key = Protector(hostId, overlayId).Unprotect(protectedKey);
            return OverlayAccessKeyDigest.HasCanonicalShape(key) ? new(key) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private IDataProtector Protector(int hostId, Guid overlayId) =>
        protection.CreateProtector(
            "BlokeBot.FullOverlayKey.v1",
            hostId.ToString(CultureInfo.InvariantCulture),
            overlayId.ToString("N")
        );
}
