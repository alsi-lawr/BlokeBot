using Microsoft.AspNetCore.SignalR;

namespace BlokeBot.Core.Features.ViewerPortal.Boundary;

internal sealed class PublicHubConnection(
    PublicDocument document,
    PublicViewerClient? client,
    Action abort,
    IDisposable? lease
) : IDisposable
{
    internal static readonly object ItemKey = new();
    private static readonly AsyncLocal<PublicHubConnection?> _current = new();
    private Action _abort = abort;
    internal static PublicHubConnection? Current => _current.Value;
    internal PublicDocument Document { get; } = document;
    internal PublicViewerClient? Client { get; } = client;

    internal void Abort() => _abort();

    internal void AttachHubAbort(Action abortHub) => _abort = abortHub;

    public void Dispose() => lease?.Dispose();

    internal async ValueTask<object?> InvokeAsync(
        HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next
    )
    {
        var previous = _current.Value;
        _current.Value = this;
        try
        {
            return await next(context);
        }
        finally
        {
            _current.Value = previous;
        }
    }
}

internal sealed class PublicHubAdmissionFilter : IHubFilter
{
    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (
            !context.Context.Items.TryGetValue(PublicHubConnection.ItemKey, out var value)
            || value is not PublicHubConnection connection
        )
        {
            context.Context.Abort();
            return Task.CompletedTask;
        }
        connection.AttachHubAbort(context.Context.Abort);
        return next(context);
    }

    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next
    ) =>
        context.Context.Items.TryGetValue(PublicHubConnection.ItemKey, out var value)
        && value is PublicHubConnection connection
            ? connection.InvokeAsync(context, next)
            : throw new HubException("The document connection is not admitted.");
}
