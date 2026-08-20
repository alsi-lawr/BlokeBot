using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.SiteAccess;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotAdmin(
        this IServiceCollection services,
        BotAccountAuthorizationMode botAccountAuthorization
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton(static sp =>
            BotAdminSettings.FromOptions(sp.GetRequiredService<IOptions<BlokeBotOptions>>().Value)
        );
        _ = services.AddSingleton<BotAdminService>();
        _ = services.AddSingleton<AdminHostManagementService>();
        _ = services.AddSingleton<HostedChannelDirectoryService>();
        _ = services.AddSingleton<BotAccountAuthorizationService>();
        switch (botAccountAuthorization)
        {
            case BotAccountAuthorizationMode.Disabled:
                _ = services.AddSingleton<ITokenStatusSource, UnavailableTokenStatusSource>();
                _ = services.AddSingleton<
                    IBotAccountAuthorizationPolicy,
                    DisabledBotAccountAuthorizationPolicy
                >();
                break;
            case BotAccountAuthorizationMode.Twitch:
                _ = services.AddSingleton<ITokenStatusSource, TokenStatusService>();
                _ = services.AddSingleton<
                    IBotAccountAuthorizationPolicy,
                    ConfiguredBotAccountAuthorizationPolicy
                >();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(botAccountAuthorization),
                    botAccountAuthorization,
                    "Unknown bot-account authorization mode."
                );
        }
        return services;
    }

    public static IServiceCollection AddBlokeBotSiteAccess(
        this IServiceCollection services,
        AccessListProfileEnrichmentMode profileEnrichment
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddTransient<AccessListProfileResolver>();
        switch (profileEnrichment)
        {
            case AccessListProfileEnrichmentMode.Disabled:
                _ = services.AddSingleton<
                    IAccessListProfileEnrichmentPolicy,
                    DisabledAccessListProfileEnrichmentPolicy
                >();
                break;
            case AccessListProfileEnrichmentMode.Twitch:
                _ = services.AddSingleton<
                    IAccessListProfileEnrichmentPolicy,
                    HelixAccessListProfileEnrichmentPolicy
                >();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(profileEnrichment),
                    profileEnrichment,
                    "Unknown access-list profile-enrichment mode."
                );
        }
        _ = services.AddSingleton<SiteAccessChangeNotifier>();
        _ = services.AddScoped<SiteAccessService>();
        return services;
    }
}
