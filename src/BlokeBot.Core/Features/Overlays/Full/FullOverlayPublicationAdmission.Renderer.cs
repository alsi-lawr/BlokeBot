using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed class FullOverlayPublicationAdmission(FullOverlayWidgetRegistry widgets)
    : IFullOverlayPublicationAdmission
{
    public async Task<FullOverlayAdmission> AdmitAsync(
        FullOverlayPublicationCandidate candidate,
        CancellationToken cancellationToken
    )
    {
        if (!FullOverlayDocuments.HasCoherentIdentity(candidate.Document))
        {
            return new FullOverlayAdmission.Rejected([
                new(
                    "invalid-document",
                    "The document identity is invalid.",
                    FullOverlayDiagnosticSeverity.Error
                ),
            ]);
        }
        var projected = await widgets.ProjectAsync(
            new(
                candidate.HostId,
                candidate.OverlayId,
                null,
                null,
                candidate.Document,
                FullOverlayDataMode.Sample,
                []
            ),
            cancellationToken
        );
        return new FullOverlayAdmission.Admitted(
            projected
                .SelectMany(widget =>
                    widget.Output.Match<ImmutableArray<FullOverlayDiagnostic>>(
                        _ => [],
                        _ => [],
                        _ => [],
                        _ => [],
                        _ => [],
                        _ => [],
                        unavailable => [unavailable.Diagnostic]
                    )
                )
                .ToImmutableArray()
        );
    }
}
