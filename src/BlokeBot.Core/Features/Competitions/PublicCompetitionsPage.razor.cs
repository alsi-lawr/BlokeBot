using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Competitions;

public partial class PublicCompetitionsPage
{
    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private CompetitionPublicBoard? _board;
    private bool _loaded;

    protected override async Task OnParametersSetAsync()
    {
        _board = await _service.GetPublicAsync(Channel, CancellationToken.None);
        _loaded = true;
    }
}
