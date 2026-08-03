using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosts;
using Microsoft.AspNetCore.Components.Routing;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private bool _blockedByMode;
    private long _botStatusFragmentRequest;
    private long _chatToolsFragmentRequest;
    private long _moderatorHelpFragmentRequest;
    private HostConfigState? _state;

    protected override async Task OnInitializedAsync()
    {
        _navigation.LocationChanged += OnLocationChanged;
        RequestFragmentReveal(_navigation.Uri);
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                AppEventKind.HostedChannelsChanged,
                InvokeAsync,
                ReloadForEventAsync,
                StateHasChanged
            )
        );
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [
                    AppEventKind.CommandsChanged,
                    AppEventKind.GuessingChanged,
                    AppEventKind.PointsChanged,
                    AppEventKind.CustomCommandsChanged,
                    AppEventKind.RequestBoardsChanged,
                    AppEventKind.PlayQueuesChanged,
                    AppEventKind.MomentsChanged,
                ],
                InvokeAsync,
                RefreshCommandCatalogAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private Task LoadAsync() => ObserveUiOperationAsync(nameof(LoadAsync), LoadCoreAsync);

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
            LoadStartupMessageDraft(loadedState.HostId!.Value, loadedState.StartupMessage);
            LoadCommandsDraft(loadedState.HostId!.Value, loadedState.Commands);
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

        var commandCatalogWasLoaded = _commandCatalog is not null;
        var previousPendingRuntimeTransition = _pendingRuntimeTransition;
        await LoadAsync();
        if (commandCatalogWasLoaded)
        {
            await RefreshCommandCatalogAsync();
        }

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
            _ = _toasts.Publish(new ToastRequest<StatusToastStrategy>(_runtimeStatusMessage));
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = InvokeAsync(() =>
        {
            RequestFragmentReveal(args.Location);
            StateHasChanged();
        });

    private Task OnNativeFragmentChangedAsync(string location)
    {
        RequestFragmentReveal(location);
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void RequestFragmentReveal(string location)
    {
        var fragment = new Uri(location).Fragment.TrimStart('#');
        switch (Uri.UnescapeDataString(fragment))
        {
            case "bot-status":
                _botStatusFragmentRequest++;
                break;
            case "chat-tools":
                _chatToolsFragmentRequest++;
                break;
            case "moderator-help":
                _moderatorHelpFragmentRequest++;
                break;
        }
    }
}
