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
        ["native-shoutouts"] = "/twitch-operations/shoutouts",
        ["native-polls"] = "/twitch-operations/polls",
        ["native-clips-markers"] = "/twitch-operations/clips-markers",
        ["native-channel-points"] = "/twitch-operations/channel-points",
        ["native-predictions"] = "/twitch-operations/predictions",
        ["guessing-leaderboard"] = $"/guessing/leaderboard/{SimulationMode.Login}",
        ["points-leaderboard"] = $"/points/leaderboard/{SimulationMode.Login}",
    };

    public static string PathFor(string? view)
    {
        return view is not null && _paths.TryGetValue(view, out var path) ? path : "/";
    }
}
