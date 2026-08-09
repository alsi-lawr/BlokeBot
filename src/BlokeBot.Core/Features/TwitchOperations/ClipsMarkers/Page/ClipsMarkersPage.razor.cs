using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.ClipsMarkers.Page;

public partial class ClipsMarkersPage
{
    private readonly HashSet<ClipMarkerStage> _openStages = [];
    private bool _clipHasDelay;
    private bool _stagesSeeded;
    private string _markerDescription = string.Empty;

    private enum ClipMarkerStage
    {
        Marker,
        Attempts,
    }

    private bool _hasAttentionRequired =>
        State?.PendingClips.Count > 0
        || State?.Results.Any(static clip => clip.Status == "Ambiguous") == true
        || State?.Markers.Any(static marker => marker.Status == "Ambiguous") == true;

    private string _markerSummary =>
        State is not { Markers.Count: > 0 } state
            ? "No markers placed yet"
            : $"Last marker: “{state.Markers[0].Description}”";

    private string _attemptsSummary
    {
        get
        {
            if (State is null)
            {
                return string.Empty;
            }

            if (_hasAttentionRequired)
            {
                return "Needs attention · something is pending or unconfirmed";
            }

            var clips = State.Results.Count;
            var markers = State.Markers.Count;
            return clips == 0 && markers == 0
                ? "No clip or marker attempts yet"
                : $"{clips} clips · {markers} markers";
        }
    }

    private bool IsStageOpen(ClipMarkerStage stage) => _openStages.Contains(stage);

    private void SetStage(ClipMarkerStage stage, bool open) =>
        _ = open ? _openStages.Add(stage) : _openStages.Remove(stage);

    protected override HostFeatureFlags Feature => HostFeatureFlags.ClipsAndMarkers;

    protected override async Task<ClipMarkerDashboardState?> LoadStateAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var state = await _clipsMarkers.LoadAsync(hostId, cancellationToken);
        if (!_stagesSeeded && state is not null)
        {
            _stagesSeeded = true;
            var needsAttention =
                state.PendingClips.Count > 0
                || state.Results.Any(static clip => clip.Status == "Ambiguous")
                || state.Markers.Any(static marker => marker.Status == "Ambiguous");
            if (needsAttention)
            {
                _ = _openStages.Add(ClipMarkerStage.Attempts);
            }
        }

        return state;
    }

    private Task CreateClipAsync() =>
        MutateAsync(hostId =>
            _clipsMarkers.CreateClipAsync(hostId, _clipHasDelay, CancellationToken.None)
        );

    private Task CreateMarkerAsync() =>
        MutateAsync(hostId =>
            _clipsMarkers.CreateMarkerAsync(hostId, _markerDescription, CancellationToken.None)
        );

    private Task RetryClipAsync(ClipAttemptReference attempt) =>
        MutateAsync(hostId =>
            _clipsMarkers.RetryClipAsync(hostId, attempt, CancellationToken.None)
        );

    private Task RetryMarkerAsync(StreamMarkerAttemptReference attempt) =>
        MutateAsync(hostId =>
            _clipsMarkers.RetryMarkerAsync(hostId, attempt, CancellationToken.None)
        );

    private Task MutateAsync(Func<int, Task<ClipMarkerOperationOutcome>> operation) =>
        MutateAsync(async hostId => Publish(await operation(hostId)));

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
            ClipMarkerOperationOutcome.NotReady => (
                "Reconnect this channel to Twitch, then try again.",
                false
            ),
            ClipMarkerOperationOutcome.InvalidRequest invalid => (invalid.Message, false),
            ClipMarkerOperationOutcome.Offline => (
                "Go live before creating a clip or marker.",
                false
            ),
            ClipMarkerOperationOutcome.VodsDisabled => (
                "Turn on stream recordings and clips in Twitch, then try again.",
                false
            ),
            ClipMarkerOperationOutcome.RerunOrPremiere => (
                "Clips and markers are not available during a rerun or premiere.",
                false
            ),
            ClipMarkerOperationOutcome.ClipAmbiguous => (
                "Twitch did not confirm whether the clip was made. Use Check outcome on this attempt.",
                false
            ),
            ClipMarkerOperationOutcome.MarkerAmbiguous => (
                "Twitch did not confirm whether the marker was made. Use Check outcome on this attempt.",
                false
            ),
            ClipMarkerOperationOutcome.ProviderRejected => (
                "Twitch could not complete that action. Check the stream and try again.",
                false
            ),
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
        Publish(message, success);
    }
}
