namespace BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;

public abstract record ClipMarkerOperationOutcome
{
    private ClipMarkerOperationOutcome() { }

    public sealed record ClipPending(ClipView Clip) : ClipMarkerOperationOutcome;

    public sealed record ClipAvailable(ClipView Clip) : ClipMarkerOperationOutcome;

    public sealed record ClipFailed(ClipView Clip) : ClipMarkerOperationOutcome;

    public sealed record MarkerCreated(StreamMarkerView Marker) : ClipMarkerOperationOutcome;

    public sealed record MarkerFailed(StreamMarkerView Marker) : ClipMarkerOperationOutcome;

    public sealed record NotReady(string Message) : ClipMarkerOperationOutcome;

    public sealed record InvalidRequest(string Message) : ClipMarkerOperationOutcome;

    public sealed record Offline : ClipMarkerOperationOutcome;

    public sealed record VodsDisabled : ClipMarkerOperationOutcome;

    public sealed record RerunOrPremiere : ClipMarkerOperationOutcome;

    public sealed record ClipAmbiguous(ClipAttemptReference Attempt) : ClipMarkerOperationOutcome;

    public sealed record MarkerAmbiguous(StreamMarkerAttemptReference Attempt)
        : ClipMarkerOperationOutcome;

    public sealed record ProviderRejected(string Message) : ClipMarkerOperationOutcome;
}
