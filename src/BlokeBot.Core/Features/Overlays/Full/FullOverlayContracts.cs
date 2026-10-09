using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.Overlays.Full;

public readonly record struct FullOverlayRevision(long Value);

public readonly record struct FullOverlayVersion(long Value);

public sealed record CreateFullOverlayCommand(string Name, FullOverlayDocument Document);

public sealed record SaveFullOverlayCommand(
    Guid OverlayId,
    FullOverlayRevision ExpectedRevision,
    string Name,
    FullOverlayDocument Document
);

public sealed record FullOverlayMutation(Guid OverlayId, FullOverlayRevision ExpectedRevision);

public sealed record RollbackFullOverlayCommand(
    Guid OverlayId,
    FullOverlayRevision ExpectedRevision,
    FullOverlayVersion Version
);

public sealed record ForgetFullOverlayVersionCommand(
    Guid OverlayId,
    FullOverlayRevision ExpectedRevision,
    FullOverlayVersion Version
);

public sealed record DuplicateFullOverlayCommand(
    Guid OverlayId,
    FullOverlayRevision ExpectedRevision,
    string Name
);

public enum FullOverlayDeleteConfirmation
{
    Unconfirmed,
    Confirmed,
}

public sealed record DeleteFullOverlayCommand(
    Guid OverlayId,
    FullOverlayRevision ExpectedRevision,
    FullOverlayDeleteConfirmation Confirmation
);

public sealed record FullOverlayView(
    Guid Id,
    string Name,
    FullOverlayDocument Draft,
    bool IsArchived,
    FullOverlayRevision Revision,
    FullOverlayVersion? PublishedVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public sealed record FullOverlayPublicationView(
    FullOverlayVersion Version,
    FullOverlayDocument Document,
    string AuthorUserId,
    string AuthorLogin,
    DateTimeOffset PublishedAtUtc
);

public sealed record FullOverlayCreation(
    FullOverlayView Overlay,
    FullOverlayPrivateAccess PrivateAccess
);

public sealed record FullOverlayPublicationSelection(
    FullOverlayView Overlay,
    ImmutableArray<FullOverlayDiagnostic> Warnings
);

public sealed class FullOverlayPrivateAccess
{
    internal FullOverlayPrivateAccess(string accessKey) => AccessKey = accessKey;

    [JsonIgnore]
    public string AccessKey { get; }

    [JsonIgnore]
    public string RelativeUrl => $"/full-overlay/{AccessKey}";

    public override string ToString() => "[REDACTED FULL OVERLAY ACCESS]";
}

public enum FullOverlayRejectionKind
{
    Invalid,
    NotFound,
    Conflict,
    Unauthorized,
    FeatureDisabled,
    Archived,
    PublicationUnavailable,
    PublicationRejected,
    SelectedVersion,
    ConfirmationRequired,
}

public sealed record FullOverlayRejection(
    FullOverlayRejectionKind Kind,
    ImmutableArray<FullOverlayDiagnostic> Diagnostics
);

public abstract record FullOverlayResult<T>
{
    private FullOverlayResult() { }

    public abstract TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<Rejected, TResult> rejected
    );

    public sealed record Succeeded(T Value) : FullOverlayResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => succeeded(this);
    }

    public sealed record Rejected(FullOverlayRejection Reason) : FullOverlayResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => rejected(this);
    }
}
