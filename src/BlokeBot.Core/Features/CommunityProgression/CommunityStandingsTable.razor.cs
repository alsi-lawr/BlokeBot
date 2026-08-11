using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CommunityProgression;

/// <summary>
/// Season standings in the public points leaderboard table idiom, ranked with RankIndicator.
/// </summary>
public partial class CommunityStandingsTable
{
    [Parameter, EditorRequired]
    public required IReadOnlyList<CommunityStandingView> Standings { get; set; }
}
