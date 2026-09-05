using Microsoft.AspNetCore.Components.Server.Circuits;

namespace BlokeBot.Core.Features.ViewerPortal;

internal sealed class PortalCircuitConnection : CircuitHandler
{
    internal event Func<bool, Task>? ConnectionChanged;

    public override Task OnConnectionDownAsync(
        Circuit circuit,
        CancellationToken cancellationToken
    ) => NotifyAsync(false);

    public override Task OnConnectionUpAsync(
        Circuit circuit,
        CancellationToken cancellationToken
    ) => NotifyAsync(true);

    public override Task OnCircuitClosedAsync(
        Circuit circuit,
        CancellationToken cancellationToken
    ) => NotifyAsync(false);

    private Task NotifyAsync(bool connected) =>
        ConnectionChanged is { } handlers
            ? Task.WhenAll(
                handlers
                    .GetInvocationList()
                    .Cast<Func<bool, Task>>()
                    .Select(handler => handler(connected))
            )
            : Task.CompletedTask;
}
