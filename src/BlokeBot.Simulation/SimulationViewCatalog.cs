namespace BlokeBot.Simulation;

internal static class SimulationViewCatalog
{
    private static readonly IReadOnlyDictionary<string, string> _paths = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["home"] = "/",
        ["channel-setup"] = "/host",
        ["alerts"] = "/alerts",
        ["guessing"] = "/guessing",
        ["guessing-settings"] = "/guessing/settings",
        ["points"] = "/points",
        ["points-settings"] = "/points/settings",
        ["custom-commands"] = "/custom-commands/settings",
        ["admin"] = "/admin",
        ["guessing-leaderboard"] =
            $"/guessing/leaderboard/{FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Login}",
        ["points-leaderboard"] =
            $"/points/leaderboard/{FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Login}",
    };

    public static string PathFor(string? view)
    {
        return view is not null && _paths.TryGetValue(view, out var path) ? path : "/";
    }
}
