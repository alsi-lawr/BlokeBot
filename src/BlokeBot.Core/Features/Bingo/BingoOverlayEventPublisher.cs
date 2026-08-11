using BlokeBot.Core.Features.Overlays;

namespace BlokeBot.Core.Features.Bingo;

internal sealed class BingoOverlayEventPublisher(IEnumerable<IOverlayEventPresenter> presenters)
    : IBingoOverlayEventObserver
{
    private readonly IOverlayEventPresenter[] _presenters = [.. presenters];

    public async ValueTask BingoEventAsync(
        BingoOverlayEvent value,
        CancellationToken cancellationToken
    )
    {
        var sourceKey = $"{value.GameId.Value:N}:{value.OperationKey}";
        foreach (var presenter in _presenters)
        {
            await presenter.PresentAsync(
                new OverlayEventPresentation.BingoEvent
                {
                    HostId = value.HostId,
                    SourceKey = sourceKey,
                    Summary = value.PublicSummary,
                },
                cancellationToken
            );
        }
    }
}
