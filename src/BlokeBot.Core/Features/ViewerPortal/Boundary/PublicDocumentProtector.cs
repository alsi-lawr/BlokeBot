using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.ViewerPassports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;

namespace BlokeBot.Core.Features.ViewerPortal.Boundary;

internal sealed record PublicDocument(bool IsPublic, string Nonce, string? Subject);

internal sealed record PublicDocumentBootstrap(PublicDocument Document, string Marker);

// Classification only: this marker never grants page, feature, host, or administrator access.
internal sealed class PublicDocumentProtector(IDataProtectionProvider protection)
{
    internal const string QueryParameter = "document";
    internal static readonly object BootstrapKey = new();
    internal static readonly object ConnectionDocumentKey = new();
    private readonly IDataProtector _protector = protection.CreateProtector(
        "BlokeBot.PublicDocument.v1"
    );

    internal PublicDocumentBootstrap Create(bool isPublic, ClaimsPrincipal principal)
    {
        var document = new PublicDocument(
            isPublic,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)),
            Subject(principal)
        );
        return new(document, _protector.Protect(JsonSerializer.Serialize(document)));
    }

    internal PublicDocument? Read(string marker, ClaimsPrincipal principal)
    {
        if (marker.Length is 0 or > 1024)
        {
            return null;
        }
        try
        {
            var document = JsonSerializer.Deserialize<PublicDocument>(_protector.Unprotect(marker));
            return
                document is { Nonce.Length: 24 }
                && document.Subject == Subject(principal)
                && (
                    document.IsPublic
                    || (principal.Identity?.IsAuthenticated == true && document.Subject is not null)
                )
                ? document
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Subject(ClaimsPrincipal principal)
    {
        var subject =
            principal.Identity?.IsAuthenticated == true
                ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;
        return subject is { Length: > 0 and <= 64 } ? subject : null;
    }

    internal static bool IsPublicPage(
        Type pageType,
        IEnumerable<KeyValuePair<string, object?>> routeValues
    ) =>
        pageType.GetCustomAttribute<LayoutAttribute>()?.LayoutType == typeof(PublicPortalLayout)
        || (
            pageType == typeof(ViewerPassportsPage)
            && routeValues.Any(value =>
                string.Equals(value.Key, "Channel", StringComparison.OrdinalIgnoreCase)
                && value.Value is not null
            )
        );
}
