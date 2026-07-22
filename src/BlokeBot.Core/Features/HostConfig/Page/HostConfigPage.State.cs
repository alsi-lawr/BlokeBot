using System.Diagnostics;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private bool _blockedByMode;
    private HostConfigState? _state;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            _events.SubscribeForComponentRefresh(
                AppEventKind.HostedChannelsChanged,
                InvokeAsync,
                ReloadForEventAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private Task LoadAsync()
    {
        return ObserveUiOperationAsync(nameof(LoadAsync), LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        var pageContext = await LoadPageContextAsync();
        var session = pageContext.Session;
        var selection = session.State.Match<BotHostSelection?>(
            _ => null,
            selected => selected.Selection,
            _ => null
        );
        if (pageContext.IsBotAccount)
        {
            _blockedByMode = true;
            _state = null;
            ClearAccessEntries();
            return;
        }

        var result = await _hostConfig.Load(session).ExecuteAsync(CancellationToken.None);
        _state = result.Match(
            option => option.Match<HostConfigState?>(value => value, () => null),
            _ => throw new UnreachableException()
        );

        _blockedByMode =
            selection is not null
            && !session.CanManageSelectedHostConfig
            && _state?.IsHostCreated == true;
        if (_blockedByMode)
        {
            _state = null;
            ClearAccessEntries();
            return;
        }

        if (_state is { IsHostCreated: true } loadedState)
        {
            await LoadAccessEntriesAsync(loadedState.ModAccess);
        }
        else
        {
            ClearAccessEntries();
        }
    }

    private async Task ReloadForEventAsync()
    {
        if (_allowModsByDefaultSaves.HasPendingSubmission)
        {
            return;
        }

        var previousPendingRuntimeTransition = _pendingRuntimeTransition;
        await LoadAsync();

        if (previousPendingRuntimeTransition is null)
        {
            return;
        }

        var currentRuntimeTransition = PendingTransition(_runtimeLifecycle);
        if (currentRuntimeTransition == previousPendingRuntimeTransition)
        {
            return;
        }

        TrackPendingRuntimeTransition();
        if (_runtimeLifecycle is not null)
        {
            _toasts.Publish(new ToastRequest<StatusToastStrategy>(_runtimeStatusMessage));
        }
    }
}
