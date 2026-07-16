using Microsoft.AspNetCore.Hosting.Server.Features;

namespace BlokeBot.Hosting;

internal sealed record BlokeBotTwitchConfigurationField(
    string ConfigurationKey,
    string EnvironmentKey
);

internal static class BlokeBotTwitchConfiguration
{
    private static readonly IReadOnlyList<BlokeBotTwitchConfigurationField> _requiredFields =
    [
        new("TwitchBot:Identity:BotUsername", "TwitchBot__Identity__BotUsername"),
        new("TwitchBot:Identity:ClientId", "TwitchBot__Identity__ClientId"),
        new("TwitchBot:Identity:ClientSecret", "TwitchBot__Identity__ClientSecret"),
        new("TwitchBot:Identity:RedirectUri", "TwitchBot__Identity__RedirectUri"),
    ];

    internal static IReadOnlyList<BlokeBotTwitchConfigurationField> MissingFields(
        IConfiguration configuration
    )
    {
        return _requiredFields
            .Where(field => string.IsNullOrWhiteSpace(configuration[field.ConfigurationKey]))
            .ToArray();
    }

    internal static string OfflineGuidance(
        IReadOnlyList<BlokeBotTwitchConfigurationField> missingFields
    )
    {
        var lines = new List<string>
        {
            "Twitch features are offline because required configuration is missing.",
            "Set these fields and restart blokebot:",
        };
        lines.AddRange(missingFields.Select(field => $"  - {field.EnvironmentKey}"));
        return string.Join(Environment.NewLine, lines);
    }
}

internal static class BlokeBotServerUrlPolicy
{
    internal const string DefaultUrl = "http://127.0.0.1:8080";

    internal static bool HasExplicitConfiguration(IConfiguration configuration)
    {
        return HasValue(configuration, "urls")
            || HasValue(configuration, "http_ports")
            || HasValue(configuration, "https_ports")
            || configuration.GetSection("Kestrel:Endpoints").GetChildren().Any();
    }

    internal static string LocalUrl(IServerAddressesFeature? addresses)
    {
        var address = addresses
            ?.Addresses.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (
            string.IsNullOrWhiteSpace(address)
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
        )
        {
            return DefaultUrl;
        }

        var host = uri.Host;
        if (host is "*" or "+" or "0.0.0.0" or "::" or "[::]")
        {
            host = "127.0.0.1";
        }

        var builder = new UriBuilder(uri) { Host = host };
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    private static bool HasValue(IConfiguration configuration, string key)
    {
        return !string.IsNullOrWhiteSpace(configuration[key]);
    }
}
