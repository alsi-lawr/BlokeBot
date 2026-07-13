using BlokeBot.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Features.PublicLeaderboards;

public partial class PublicPointsLeaderboardPage
{
    private const int _publicLeaderboardSize = 50;

    private PublicLeaderboardHost? _host;
    private IReadOnlyList<PointBalanceEntry>? _leaderboard;
    private bool _loaded;

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private bool _featureEnabled =>
        _host is not null
        && (_host.EnabledFeatures & HostFeatureFlags.Points) == HostFeatureFlags.Points;

    private string _headerDescription =>
        _host is null
            ? "Read-only point balances."
            : $"Read-only point balances for {_host.DisplayName}.";

    protected override async Task OnParametersSetAsync()
    {
        _loaded = false;
        _leaderboard = null;
        _host = await _hosts.FindAsync(Channel, CancellationToken.None);

        if (_featureEnabled)
        {
            _leaderboard = await _balances.GetLeaderboardAsync(
                _host!.Id,
                _publicLeaderboardSize,
                CancellationToken.None
            );
        }

        _loaded = true;
    }
}
