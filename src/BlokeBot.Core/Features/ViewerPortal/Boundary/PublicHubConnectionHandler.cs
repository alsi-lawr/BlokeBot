using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.ViewerPortal.Boundary;

// The framework connection lifetime starts before handshake and spans successive long polls.
internal sealed class PublicHubConnectionHandler<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicMethods
    )]
        THub
>(
    HubLifetimeManager<THub> lifetime,
    IHubProtocolResolver protocols,
    IOptions<HubOptions> globalOptions,
    IOptions<HubOptions<THub>> hubOptions,
    ILoggerFactory logging,
    IUserIdProvider users,
    IServiceScopeFactory scopes,
    PublicViewerAdmission admission
)
    : HubConnectionHandler<THub>(
        lifetime,
        protocols,
        globalOptions,
        hubOptions,
        logging,
        users,
        scopes
    )
    where THub : Hub
{
    public override async Task OnConnectedAsync(ConnectionContext context)
    {
        var http = context.GetHttpContext();
        if (
            http?.Items[PublicDocumentProtector.ConnectionDocumentKey]
            is not PublicDocument document
        )
        {
            await base.OnConnectedAsync(context);
            return;
        }
        var client = http.Connection.RemoteIpAddress is { } address
            ? new PublicViewerClient(address, document.Subject)
            : null;
        var lease =
            document.IsPublic && client is not null
                ? admission.TryAcquire(client, PublicViewerLeaseKind.Transport)
                : null;
        if (document.IsPublic && lease is null)
        {
            context.Abort();
            return;
        }
        using var connection = new PublicHubConnection(document, client, context.Abort, lease);
        context.Items[PublicHubConnection.ItemKey] = connection;
        http.Items[PublicHubConnection.ItemKey] = connection;
        await base.OnConnectedAsync(context);
    }
}
