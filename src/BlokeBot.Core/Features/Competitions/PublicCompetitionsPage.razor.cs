using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Competitions;

public partial class PublicCompetitionsPage
{
    [Inject]
    private BlokeBot.Core.Features.ViewerPortal.Boundary.PublicViewerGate _publicGate { get; set; } =
        null!;

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private CompetitionPublicBoard? _board;
    private bool _loaded;

    protected override async Task OnParametersSetAsync()
    {
        _board = null;
        if (!await _publicGate.TryReadAsync(Channel, CancellationToken.None))
        {
            _loaded = true;
            return;
        }
        _board = await _service.GetPublicAsync(Channel, CancellationToken.None);
        _loaded = true;
    }
}
