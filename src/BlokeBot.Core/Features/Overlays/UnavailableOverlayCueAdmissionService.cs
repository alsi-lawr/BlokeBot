namespace BlokeBot.Core.Features.Overlays;

internal sealed class UnavailableOverlayCueAdmissionService : IOverlayCueAdmissionService
{
    public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
        OverlayCueReferenceRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<OverlayCueReferenceOutcome>(
            new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Parent)
        );
    }

    public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OverlayCueAdmissionCatalog([], []));
    }

    public Task<OverlayCueAdmissionOutcome> AdmitAsync(
        OverlayCueAdmissionRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<OverlayCueAdmissionOutcome>(
            new OverlayCueAdmissionOutcome.Missing()
        );
    }
}
