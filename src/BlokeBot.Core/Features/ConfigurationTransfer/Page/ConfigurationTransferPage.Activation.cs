using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Page;

public partial class ConfigurationTransferPage : IAsyncDisposable
{
    private CancellationTokenSource? _activationPoll;

    private void StartActivationPolling()
    {
        _activationPoll?.Cancel();
        if (_applied?.ActivationId is not { } activationId)
        {
            return;
        }

        _activationPoll = new CancellationTokenSource();
        _ = PollActivationAsync(activationId, _activationPoll.Token);
    }

    private async Task PollActivationAsync(Guid activationId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                var current = await _activations.LoadAsync(HostId, activationId, cancellationToken);
                if (current is null)
                {
                    return;
                }

                await InvokeAsync(() =>
                {
                    _activation = current;
                    StateHasChanged();
                });
                if (
                    current.Status
                    is ConfigurationActivationStatus.Complete
                        or ConfigurationActivationStatus.Failed
                        or ConfigurationActivationStatus.ManualFollowUp
                )
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public ValueTask DisposeAsync()
    {
        _activationPoll?.Cancel();
        _activationPoll?.Dispose();
        return ValueTask.CompletedTask;
    }
}
