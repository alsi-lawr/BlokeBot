using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Functional;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlokeBot.Core.Features.ViewerPortal.Boundary;

internal sealed class PublicViewerGate(
    PublicViewerAdmission admission,
    PublicViewerCircuit circuit,
    PublicLeaderboardHostLookup hosts,
    AuthenticationStateProvider authentication,
    IHttpContextAccessor http
)
{
    internal string? Notice { get; private set; }
    internal event Action? Changed;

    internal async Task<bool> TryReadAsync(string channel, CancellationToken ct)
    {
        var client = await ClientAsync(publicRead: true);
        if (client is null || !admission.TryAttempt(client, PublicViewerAttempt.Read))
        {
            return Limited();
        }
        var host = await hosts.Find(channel).RunAsync(ct);
        return host.Match(value => admission.TryChannelRead(client, value.Id), static () => true)
            ? Accepted()
            : Limited();
    }

    internal async Task<bool> TryReadResolvedAsync(int hostId)
    {
        var client = await ClientAsync(publicRead: true);
        return client is not null && admission.TryAttempt(client, PublicViewerAttempt.Read, hostId)
            ? Accepted()
            : Limited();
    }

    internal async Task<bool> TryActionAsync(int hostId)
    {
        var client = await ClientAsync(publicRead: false);
        return
            client is not null && admission.TryAttempt(client, PublicViewerAttempt.Action, hostId)
            ? Accepted()
            : Limited();
    }

    private async Task<PublicViewerClient?> ClientAsync(bool publicRead)
    {
        var principal =
            circuit.Connection is null && http.HttpContext is { } request
                ? request.User
                : (await authentication.GetAuthenticationStateAsync()).User;
        var subject = PublicDocumentProtector.Subject(principal);
        if (principal.Identity?.IsAuthenticated == true && subject is null)
        {
            if (!publicRead)
            {
                return null;
            }
            // Only charge a public read; the feature owner still receives the original stale session.
            subject = circuit.Connection?.Document.Subject;
        }
        if (circuit.Connection is { } connection)
        {
            if (!connection.Document.IsPublic || connection.Document.Subject != subject)
            {
                connection.Abort();
                return null;
            }
            return connection.Client;
        }
        return http.HttpContext?.Connection.RemoteIpAddress is { } address
            ? new PublicViewerClient(address, subject)
            : null;
    }

    private bool Limited()
    {
        Notice =
            "This page is busy. Wait a minute, then try again. If your sign-in changed, reload the page.";
        Changed?.Invoke();
        return false;
    }

    private bool Accepted()
    {
        if (Notice is not null)
        {
            Notice = null;
            Changed?.Invoke();
        }
        return true;
    }
}
