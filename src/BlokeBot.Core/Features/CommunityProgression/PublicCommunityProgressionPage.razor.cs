using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CommunityProgression;

public partial class PublicCommunityProgressionPage
{
    [Inject]
    private BlokeBot.Core.Features.ViewerPortal.Boundary.PublicViewerGate _publicGate { get; set; } =
        null!;

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private CommunityPublicView? _view;
    private bool _loaded;

    private DateTime _nowUtc => _clock.GetUtcNow().UtcDateTime;

    protected override async Task OnParametersSetAsync()
    {
        _view = null;
        if (!await _publicGate.TryReadAsync(Channel, CancellationToken.None))
        {
            _loaded = true;
            return;
        }
        _view = await _progression.GetPublicAsync(Channel, CancellationToken.None);
        _loaded = true;
    }

    private string SeasonSummary(CommunityPublicSeasonView season)
    {
        var range = CommunityProgressionPresentation.SeasonRange(
            season.StartsAtUtc,
            season.EndsAtUtc,
            _nowUtc
        );
        return season.Status switch
        {
            CommunitySeasonStatus.Closed => $"{range}. Closed, standings snapshotted.",
            CommunitySeasonStatus.Archived =>
                $"{range}. Archived, final standings and completion history retained.",
            _ => string.IsNullOrWhiteSpace(season.Description) ? range : season.Description,
        };
    }
}
