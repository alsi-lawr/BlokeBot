using System.Diagnostics;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.PublicLeaderboards;

public partial class PublicPointsLeaderboardPage
{
    [Inject]
    private BlokeBot.Core.Features.ViewerPortal.Boundary.PublicViewerGate _publicGate { get; set; } =
        null!;

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
        _leaderboard = null;
        if (!await _publicGate.TryReadAsync(Channel, CancellationToken.None))
        {
            _loaded = true;
            return;
        }
        _loaded = false;
        _leaderboard = null;
        var host = await _hosts.Find(Channel).ExecuteAsync(CancellationToken.None);
        _host = host.Match(
            static option =>
                option.Match<PublicLeaderboardHost?>(static value => value, static () => null),
            static _ => throw new UnreachableException()
        );

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
