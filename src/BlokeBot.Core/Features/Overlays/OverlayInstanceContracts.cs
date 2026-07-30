using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

public readonly record struct OverlayRevision(long Value);

public sealed record CreateOverlayInstanceCommand(
    string Name,
    OverlayType Type,
    OverlayConfiguration Configuration
);

public sealed record RenameOverlayInstanceCommand(
    Guid OverlayId,
    OverlayRevision ExpectedRevision,
    string Name
);

public sealed record ConfigureOverlayInstanceCommand(
    Guid OverlayId,
    OverlayRevision ExpectedRevision,
    OverlayConfiguration Configuration
);

public sealed record ChangeOverlayInstanceAvailabilityCommand(
    Guid OverlayId,
    OverlayRevision ExpectedRevision
);

public sealed record RotateOverlayInstanceKeyCommand(
    Guid OverlayId,
    OverlayRevision ExpectedRevision
);

public sealed record DeleteOverlayInstanceCommand(Guid OverlayId, OverlayRevision ExpectedRevision);

public sealed record OverlayInstanceView(
    Guid Id,
    string Name,
    OverlayType Type,
    bool IsEnabled,
    OverlayConfiguration Configuration,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    OverlayRevision Revision
);

public sealed record OverlayInstanceCreation(
    OverlayInstanceView Instance,
    OverlayPrivateAccess PrivateAccess
);

public sealed record OverlayInstanceKeyRotation(
    OverlayInstanceView Instance,
    OverlayPrivateAccess PrivateAccess
);

public sealed class OverlayPrivateAccess
{
    internal OverlayPrivateAccess(string accessKey)
    {
        AccessKey = accessKey;
        RelativeUrl = $"/overlay/{accessKey}";
    }

    [JsonIgnore]
    public string AccessKey { get; }

    [JsonIgnore]
    public string RelativeUrl { get; }

    public override string ToString()
    {
        return "[REDACTED OVERLAY ACCESS]";
    }
}

public abstract record OverlayInstanceResult<T>
{
    private OverlayInstanceResult() { }

    public abstract TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<Rejected, TResult> rejected
    );

    public sealed record Succeeded(T Value) : OverlayInstanceResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        )
        {
            return succeeded(this);
        }
    }

    public sealed record Rejected(OverlayInstanceRejection Reason) : OverlayInstanceResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        )
        {
            return rejected(this);
        }
    }
}

public abstract record OverlayInstanceRejection
{
    private OverlayInstanceRejection() { }

    public abstract string Message { get; }

    public sealed record Invalid(string Detail) : OverlayInstanceRejection
    {
        public override string Message => Detail;
    }

    public sealed record NotFound : OverlayInstanceRejection
    {
        public override string Message => "The overlay instance was not found.";
    }

    public sealed record Conflict : OverlayInstanceRejection
    {
        public override string Message => "The overlay instance changed. Reload it and try again.";
    }

    public sealed record Unauthorized : OverlayInstanceRejection
    {
        public override string Message =>
            "The selected channel does not grant overlay management access.";
    }

    public sealed record AuthorityUnavailable : OverlayInstanceRejection
    {
        public override string Message =>
            "Moderator access could not be confirmed. Try again later.";
    }
}

public sealed record ResolvedOverlayInstance(
    int HostId,
    Guid OverlayId,
    OverlayType Type,
    OverlayConfiguration Configuration,
    OverlayRevision Revision
);

public abstract record OverlayResolutionResult
{
    private OverlayResolutionResult() { }

    public abstract TResult Match<TResult>(
        Func<Resolved, TResult> resolved,
        Func<NotFound, TResult> notFound
    );

    public sealed record Resolved(ResolvedOverlayInstance Instance) : OverlayResolutionResult
    {
        public override TResult Match<TResult>(
            Func<Resolved, TResult> resolved,
            Func<NotFound, TResult> notFound
        )
        {
            return resolved(this);
        }
    }

    public sealed record NotFound : OverlayResolutionResult
    {
        public override TResult Match<TResult>(
            Func<Resolved, TResult> resolved,
            Func<NotFound, TResult> notFound
        )
        {
            return notFound(this);
        }
    }
}
