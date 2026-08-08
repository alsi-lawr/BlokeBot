using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.TwitchOperations.Shared;

/// <summary>
/// Shared lifecycle for the native Twitch operation dashboards: channel/operations refresh
/// subscription, the feature-gated state load with fault capture, host-authorized mutation with
/// reload, and outcome toast publication. Pages supply their feature flag, state loader, and
/// per-outcome messages.
/// </summary>
public abstract class NativeOperationPage<TState> : AuthenticatedPageComponent
    where TState : class
{
    protected TState? State { get; private set; }

    protected bool NativeTwitchEnabled { get; private set; }

    protected bool Loading { get; private set; } = true;

    protected bool LoadFailed { get; private set; }

    [Inject]
    protected ToastService Toasts { get; set; } = default!;

    [Inject]
    protected NativeTwitchFeatureGate NativeTwitch { get; set; } = default!;

    [Inject]
    protected EventBus<AppEventKind> Events { get; set; } = default!;

    protected abstract HostFeatureFlags Feature { get; }

    protected abstract Task<TState?> LoadStateAsync(
        int hostId,
        CancellationToken cancellationToken
    );

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            Events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.TwitchOperationsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    protected async Task LoadAsync()
    {
        Loading = true;
        LoadFailed = false;
        try
        {
            _ = await LoadPageContextAsync();
            NativeTwitchEnabled =
                HostId != 0
                && await NativeTwitch.IsEnabledAsync(HostId, Feature, CancellationToken.None);
            var state = NativeTwitchEnabled
                ? await LoadStateAsync(HostId, CancellationToken.None)
                : null;
            NativeTwitchEnabled = await ConfirmEnabledAfterLoadAsync(state);
            State = NativeTwitchEnabled ? state : null;
        }
        catch (Exception exception)
        {
            State = null;
            NativeTwitchEnabled = false;
            LoadFailed = true;
            ReportUiFault(nameof(LoadAsync), exception);
        }
        finally
        {
            Loading = false;
        }
    }

    /// <summary>
    /// Re-evaluated after the state load so a page whose load spans provider calls can honour a
    /// feature switch turned off during that window. The default keeps the pre-load decision.
    /// </summary>
    protected virtual Task<bool> ConfirmEnabledAfterLoadAsync(TState? state) =>
        Task.FromResult(NativeTwitchEnabled);

    protected async Task MutateAsync(Func<int, Task> operation)
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                await operation(hostId);
                await LoadAsync();
            }
        );
    }

    protected void Publish(string message, bool success)
    {
        if (success)
        {
            _ = Toasts.Publish(new ToastRequest<SuccessToastStrategy>(message));
        }
        else
        {
            Warn(message);
        }
    }

    protected void Warn(string message) =>
        _ = Toasts.Publish(new ToastRequest<WarningToastStrategy>(message));
}
