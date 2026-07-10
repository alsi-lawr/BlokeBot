using BlokeBot.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Features.PublicLeaderboards;

public partial class PublicPointsLeaderboardPage
{
    private const int PublicLeaderboardSize = 50;

    private PublicLeaderboardHost? host;
    private IReadOnlyList<PointBalanceEntry>? leaderboard;
    private bool loaded;

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private bool FeatureEnabled =>
        host is not null
        && (host.EnabledFeatures & HostFeatureFlags.Points) == HostFeatureFlags.Points;

    private string HeaderDescription =>
        host is null
            ? "Read-only point balances."
            : $"Read-only point balances for {host.DisplayName}.";

    protected override async Task OnParametersSetAsync()
    {
        loaded = false;
        leaderboard = null;
        host = await Hosts.FindAsync(Channel, CancellationToken.None);

        if (FeatureEnabled)
            leaderboard = await Balances.GetLeaderboardAsync(
                host!.Id,
                PublicLeaderboardSize,
                CancellationToken.None
            );

        loaded = true;
    }
}
