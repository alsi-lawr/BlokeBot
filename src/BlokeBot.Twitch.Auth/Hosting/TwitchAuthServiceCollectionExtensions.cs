using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Adds Twitch authentication services to an application service collection.
/// </summary>
public static class TwitchAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the low-level Twitch OAuth API client.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddTwitchOAuthApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient("twitch-oauth");
        services.TryAddSingleton<TwitchOAuthApiClient>();

        return services;
    }

    /// <summary>
    /// Registers OAuth flows and token providers for a configured Twitch bot identity.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddTwitchAuth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTwitchOAuthApi();
        services.TryAddSingleton<TwitchAppAccessTokenProvider>();
        services.TryAddSingleton<ITwitchTokenStore, JsonTwitchTokenStore>();
        services.TryAddSingleton<ITwitchOAuthStateStore, InMemoryTwitchOAuthStateStore>();
        services.TryAddSingleton<ITwitchOAuthClient, TwitchOAuthClient>();
        services.TryAddSingleton<ITwitchOAuthFlow, TwitchOAuthFlow>();
        services.TryAddSingleton<ITwitchAccessTokenCache, TwitchAccessTokenCache>();
        services.TryAddSingleton<ITwitchAccessTokenProvider, TwitchAccessTokenProvider>();

        return services;
    }
}
