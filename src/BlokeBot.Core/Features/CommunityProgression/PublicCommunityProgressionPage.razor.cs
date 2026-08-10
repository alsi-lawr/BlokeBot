using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CommunityProgression;

public partial class PublicCommunityProgressionPage
{
    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private CommunityPublicView? _view;
    private bool _loaded;

    protected override async Task OnParametersSetAsync()
    {
        _view = await _progression.GetPublicAsync(Channel, CancellationToken.None);
        _loaded = true;
    }

    private static string SeasonSummary(CommunityPublicSeasonView season)
    {
        var range = CommunityProgressionPresentation.SeasonRange(
            season.StartsAtUtc,
            season.EndsAtUtc
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
