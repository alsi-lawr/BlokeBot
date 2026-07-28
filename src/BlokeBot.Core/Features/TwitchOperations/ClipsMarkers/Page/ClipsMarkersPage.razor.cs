using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;

namespace BlokeBot.Core.Features.TwitchOperations.ClipsMarkers.Page;

public partial class ClipsMarkersPage
{
    private ClipMarkerDashboardState? _state;
    private bool _clipHasDelay;
    private string _markerDescription = string.Empty;
    private bool _nativeTwitchEnabled;
    private bool _loading = true;
    private bool _loadFailed;

    private bool _hasAttentionRequired =>
        _state?.PendingClips.Count > 0
        || _state?.Results.Any(clip => clip.Status == "Ambiguous") == true
        || _state?.Markers.Any(marker => marker.Status == "Ambiguous") == true;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.TwitchOperationsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            await LoadPageContextAsync();
            _nativeTwitchEnabled =
                HostId != 0 && await _nativeTwitch.IsEnabledAsync(HostId, CancellationToken.None);
            _state = _nativeTwitchEnabled
                ? await _clipsMarkers.LoadAsync(HostId, CancellationToken.None)
                : null;
        }
        catch (Exception exception)
        {
            _state = null;
            _nativeTwitchEnabled = false;
            _loadFailed = true;
            ReportUiFault(nameof(LoadAsync), exception);
        }
        finally
        {
            _loading = false;
        }
    }

    private Task CreateClipAsync()
    {
        return MutateAsync(hostId =>
            _clipsMarkers.CreateClipAsync(hostId, _clipHasDelay, CancellationToken.None)
        );
    }

    private Task CreateMarkerAsync()
    {
        return MutateAsync(hostId =>
            _clipsMarkers.CreateMarkerAsync(hostId, _markerDescription, CancellationToken.None)
        );
    }

    private Task RetryClipAsync(ClipAttemptReference attempt)
    {
        return MutateAsync(hostId =>
            _clipsMarkers.RetryClipAsync(hostId, attempt, CancellationToken.None)
        );
    }

    private Task RetryMarkerAsync(StreamMarkerAttemptReference attempt)
    {
        return MutateAsync(hostId =>
            _clipsMarkers.RetryMarkerAsync(hostId, attempt, CancellationToken.None)
        );
    }

    private async Task MutateAsync(Func<int, Task<ClipMarkerOperationOutcome>> operation)
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                Publish(await operation(hostId));
                await LoadAsync();
            }
        );
    }

    private void Publish(ClipMarkerOperationOutcome outcome)
    {
        var (message, success) = outcome switch
        {
            ClipMarkerOperationOutcome.ClipPending => (
                "Clip requested; Twitch is preparing it.",
                true
            ),
            ClipMarkerOperationOutcome.ClipAvailable => ("Clip is available.", true),
            ClipMarkerOperationOutcome.MarkerCreated => ("Stream marker created.", true),
            ClipMarkerOperationOutcome.NotReady notReady => (notReady.Message, false),
            ClipMarkerOperationOutcome.InvalidRequest invalid => (invalid.Message, false),
            ClipMarkerOperationOutcome.Offline => (
                "Twitch reports that the channel is offline.",
                false
            ),
            ClipMarkerOperationOutcome.VodsDisabled => (
                "Twitch reports that VOD or clip creation is disabled.",
                false
            ),
            ClipMarkerOperationOutcome.RerunOrPremiere => (
                "Twitch reports that this stream cannot create clips or markers.",
                false
            ),
            ClipMarkerOperationOutcome.ClipAmbiguous => (
                "Twitch did not confirm the clip outcome. BlokeBot retained the attempt for a safe status check.",
                false
            ),
            ClipMarkerOperationOutcome.MarkerAmbiguous => (
                "Twitch did not confirm the marker outcome. BlokeBot retained the attempt for a safe status check.",
                false
            ),
            ClipMarkerOperationOutcome.ProviderRejected rejected => (rejected.Message, false),
            ClipMarkerOperationOutcome.ClipFailed failed => (
                failed.Clip.FailureReason ?? "Twitch did not create the clip.",
                false
            ),
            ClipMarkerOperationOutcome.MarkerFailed failed => (
                failed.Marker.FailureReason ?? "Twitch did not create the marker.",
                false
            ),
            _ => throw new UnreachableException(),
        };
        if (success)
        {
            _toasts.Publish(new ToastRequest<SuccessToastStrategy>(message));
        }
        else
        {
            _toasts.Publish(new ToastRequest<WarningToastStrategy>(message));
        }
    }
}
