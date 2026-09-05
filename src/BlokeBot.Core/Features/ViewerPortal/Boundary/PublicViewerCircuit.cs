using Microsoft.AspNetCore.Components.Server.Circuits;

namespace BlokeBot.Core.Features.ViewerPortal.Boundary;

internal sealed class PublicViewerCircuit(
    PublicViewerAdmission admission,
    IHttpContextAccessor http
) : CircuitHandler, IDisposable
{
    private IDisposable? _retainedLease;
    private string? _nonce;
    private bool _isPublic;
    internal PublicHubConnection? Connection { get; private set; }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Capture();
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Capture();
        return Task.CompletedTask;
    }

    private void Capture()
    {
        Connection =
            PublicHubConnection.Current
            ?? http.HttpContext?.Items[PublicHubConnection.ItemKey] as PublicHubConnection;
        if (_nonce is not null)
        {
            Verify(_isPublic, _nonce);
        }
    }

    internal void Enter(bool isPublic, string nonce)
    {
        Verify(isPublic, nonce);
        _isPublic = isPublic;
        _nonce = nonce;
        if (isPublic && _retainedLease is null)
        {
            _retainedLease = admission.TryAcquire(
                Connection!.Client!,
                PublicViewerLeaseKind.Circuit
            );
            if (_retainedLease is null)
            {
                Reject();
            }
        }
    }

    private void Verify(bool isPublic, string nonce)
    {
        if (
            Connection is null
            || Connection.Document.IsPublic != isPublic
            || Connection.Document.Nonce != nonce
            || (isPublic && Connection.Client is null)
        )
        {
            Reject();
        }
    }

    private void Reject()
    {
        Connection?.Abort();
        throw new InvalidOperationException(
            "The document circuit is not admitted. Reload the page."
        );
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next
    ) =>
        async context =>
        {
            if (
                Connection is { Document.IsPublic: true, Client: { } client }
                && !admission.TryAttempt(client, PublicViewerAttempt.Inbound)
            )
            {
                Reject();
            }
            await next(context);
        };

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (Connection is { Document.IsPublic: true })
        {
            Connection.Abort();
        }
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _retainedLease?.Dispose();
        _retainedLease = null;
    }
}
