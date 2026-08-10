using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CommunityProgression;

/// <summary>
/// Season standings in the public points leaderboard table idiom, ranked with RankIndicator.
/// </summary>
public partial class CommunityStandingsTable
{
    private const int _visibleLimit = 20;

    [Parameter, EditorRequired]
    public required IReadOnlyList<CommunityStandingView> Standings { get; set; }

    private IEnumerable<CommunityStandingView> _visible => Standings.Take(_visibleLimit);
}
