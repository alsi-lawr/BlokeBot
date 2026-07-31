using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

public readonly record struct OverlayCueRevision(long Value);

public sealed record OverlayCueView(
    Guid Id,
    string Name,
    bool IsEnabled,
    int DurationMilliseconds,
    OverlayCueQueuePolicy QueuePolicy,
    OverlayCueConfiguration Configuration,
    OverlayCueRevision Revision,
    DateTimeOffset UpdatedAtUtc
);

public sealed record OverlayMediaAssetView(
    Guid Id,
    string Name,
    string ContentType,
    long ByteLength,
    int ContentRevision,
    DateTimeOffset UpdatedAtUtc
);

public sealed record SaveOverlayCueCommand(
    Guid? CueId,
    OverlayCueRevision ExpectedRevision,
    string Name,
    bool IsEnabled,
    int DurationMilliseconds,
    OverlayCueQueuePolicy QueuePolicy,
    string ConfigurationJson
);

public sealed record ReplaceOverlayMediaAssetCommand(
    Guid AssetId,
    int ExpectedContentRevision,
    string ClaimedContentType,
    Stream Content
);

public abstract record OverlayCueResult<T>
{
    private OverlayCueResult() { }

    public sealed record Succeeded(T Value) : OverlayCueResult<T>;

    public sealed record Rejected(OverlayCueRejection Reason) : OverlayCueResult<T>;
}

public abstract record OverlayCueRejection
{
    private OverlayCueRejection() { }

    public abstract string Message { get; }

    public sealed record Invalid(string Detail) : OverlayCueRejection
    {
        public override string Message => Detail;
    }

    public sealed record Missing : OverlayCueRejection
    {
        public override string Message => "The cue or media asset was not found.";
    }

    public sealed record Conflict : OverlayCueRejection
    {
        public override string Message => "The item changed. Reload it and try again.";
    }

    public sealed record InUse : OverlayCueRejection
    {
        public override string Message =>
            "This media asset is used by a cue. Remove the cue layer before deleting it.";
    }

    public sealed record Unauthorized : OverlayCueRejection
    {
        public override string Message =>
            "The selected channel does not grant overlay management access.";
    }

    public sealed record ParentDisabled : OverlayCueRejection
    {
        public override string Message =>
            "Overlays are off. Turn them on in Channel setup before making changes.";
    }

    public sealed record StorageUnavailable : OverlayCueRejection
    {
        public override string Message =>
            "Media storage is temporarily unavailable. No media changes were saved.";
    }
}

internal sealed record OverlayMediaContent(
    int HostId,
    Guid AssetId,
    string ContentType,
    long ByteLength,
    int ContentRevision,
    string Path
);
