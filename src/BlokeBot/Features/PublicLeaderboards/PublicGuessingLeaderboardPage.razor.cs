using BlokeBot.Features.Guessing.History;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Features.PublicLeaderboards;

public partial class PublicGuessingLeaderboardPage
{
    private const int PublicPageSize = 50;

    private PublicLeaderboardHost? host;
    private GuessLeaderboardPage? leaderboard;
    private bool loaded;

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private bool FeatureEnabled =>
        host is not null
        && (host.EnabledFeatures & HostFeatureFlags.Guessing) == HostFeatureFlags.Guessing;

    private string HeaderDescription =>
        host is null
            ? "Read-only guessing results."
            : $"Read-only guessing results for {host.DisplayName}.";

    protected override async Task OnParametersSetAsync()
    {
        loaded = false;
        leaderboard = null;
        host = await Hosts.FindAsync(Channel, CancellationToken.None);

        if (FeatureEnabled)
        {
            leaderboard = await History.LoadLeaderboardAsync(
                host!.Id,
                new GuessHistoryQuery { Page = 1, PageSize = PublicPageSize },
                CancellationToken.None
            );
        }

        loaded = true;
    }
}
