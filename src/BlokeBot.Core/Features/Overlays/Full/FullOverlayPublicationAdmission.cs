using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Overlays.Full;

public sealed record FullOverlayPublicationCandidate(
    int HostId,
    Guid OverlayId,
    FullOverlayDocument Document
);

// Registry and isolated-render admission are assembled by their owners. Missing dependencies/media
// produce public-safe warnings and widget fallbacks, not a blanket rejection of the document.
public interface IFullOverlayPublicationAdmission
{
    Task<FullOverlayAdmission> AdmitAsync(
        FullOverlayPublicationCandidate candidate,
        CancellationToken cancellationToken
    );
}

public abstract record FullOverlayAdmission
{
    private FullOverlayAdmission() { }

    public abstract TResult Match<TResult>(
        Func<Admitted, TResult> admitted,
        Func<Rejected, TResult> rejected,
        Func<Unavailable, TResult> unavailable
    );

    public sealed record Admitted(ImmutableArray<FullOverlayDiagnostic> Warnings)
        : FullOverlayAdmission
    {
        public override TResult Match<TResult>(
            Func<Admitted, TResult> admitted,
            Func<Rejected, TResult> rejected,
            Func<Unavailable, TResult> unavailable
        ) => admitted(this);
    }

    public sealed record Rejected(ImmutableArray<FullOverlayDiagnostic> Diagnostics)
        : FullOverlayAdmission
    {
        public override TResult Match<TResult>(
            Func<Admitted, TResult> admitted,
            Func<Rejected, TResult> rejected,
            Func<Unavailable, TResult> unavailable
        ) => rejected(this);
    }

    public sealed record Unavailable : FullOverlayAdmission
    {
        public override TResult Match<TResult>(
            Func<Admitted, TResult> admitted,
            Func<Rejected, TResult> rejected,
            Func<Unavailable, TResult> unavailable
        ) => unavailable(this);
    }
}

internal sealed class UnavailableFullOverlayPublicationAdmission : IFullOverlayPublicationAdmission
{
    public Task<FullOverlayAdmission> AdmitAsync(
        FullOverlayPublicationCandidate candidate,
        CancellationToken cancellationToken
    ) => Task.FromResult<FullOverlayAdmission>(new FullOverlayAdmission.Unavailable());
}
