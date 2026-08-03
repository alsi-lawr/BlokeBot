using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components;

public partial class RankIndicator
{
    [Parameter]
    public int Rank { get; set; }

    private string _indicatorClass =>
        Rank switch
        {
            1 => "rank-indicator rank-indicator--gold",
            2 => "rank-indicator rank-indicator--silver",
            3 => "rank-indicator rank-indicator--bronze",
            _ => "rank-indicator",
        };

    private string _label =>
        Rank switch
        {
            1 => "First place",
            2 => "Second place",
            3 => "Third place",
            _ => $"Rank {Rank}",
        };
}
