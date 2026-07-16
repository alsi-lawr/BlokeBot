using BlokeBot.Core.Hosting;

namespace BlokeBot.Hosting;

internal sealed record BlokeBotTwitchModeSelection(
    BlokeBotRuntimeMode Mode,
    IReadOnlyList<string> MissingEnvironmentKeys
)
{
    private static readonly IReadOnlyList<RequiredTwitchConfiguration> _requiredFields =
    [
        new("TwitchBot:Identity:BotUsername", "TwitchBot__Identity__BotUsername"),
        new("TwitchBot:Identity:ClientId", "TwitchBot__Identity__ClientId"),
        new("TwitchBot:Identity:ClientSecret", "TwitchBot__Identity__ClientSecret"),
        new("TwitchBot:Identity:RedirectUri", "TwitchBot__Identity__RedirectUri"),
    ];

    internal static BlokeBotTwitchModeSelection FromConfiguration(IConfiguration configuration)
    {
        var missingEnvironmentKeys = _requiredFields
            .Where(field => string.IsNullOrWhiteSpace(configuration[field.ConfigurationKey]))
            .Select(field => field.EnvironmentKey)
            .ToArray();

        return new BlokeBotTwitchModeSelection(
            missingEnvironmentKeys.Length == 0
                ? BlokeBotRuntimeMode.Online
                : BlokeBotRuntimeMode.Offline,
            Array.AsReadOnly(missingEnvironmentKeys)
        );
    }

    internal string OfflineGuidance()
    {
        var lines = new List<string>
        {
            "Twitch features are offline because required configuration is missing.",
            "Set these fields and restart blokebot:",
        };
        lines.AddRange(MissingEnvironmentKeys.Select(key => $"  - {key}"));
        return string.Join(Environment.NewLine, lines);
    }

    private sealed record RequiredTwitchConfiguration(
        string ConfigurationKey,
        string EnvironmentKey
    );
}
