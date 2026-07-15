using System.Diagnostics;
using BlokeBot.Features.Guessing.History;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Features.PublicLeaderboards;

public partial class PublicGuessingLeaderboardPage
{
    private const int _publicPageSize = 50;

    private PublicLeaderboardHost? _host;
    private GuessLeaderboardPage? _leaderboard;
    private bool _loaded;

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    private bool _featureEnabled =>
        _host is not null
        && (_host.EnabledFeatures & HostFeatureFlags.Guessing) == HostFeatureFlags.Guessing;

    private string _headerDescription =>
        _host is null
            ? "Read-only guessing results."
            : $"Read-only guessing results for {_host.DisplayName}.";

    protected override async Task OnParametersSetAsync()
    {
        _loaded = false;
        _leaderboard = null;
        var host = await _hosts.Find(Channel).ExecuteAsync(CancellationToken.None);
        _host = host.Match(
            option => option.Match<PublicLeaderboardHost?>(value => value, () => null),
            _ => throw new UnreachableException()
        );

        if (_featureEnabled)
        {
            _leaderboard = await _history.LoadLeaderboardAsync(
                _host!.Id,
                new GuessHistoryQuery { Page = 1, PageSize = _publicPageSize },
                CancellationToken.None
            );
        }

        _loaded = true;
    }
}
