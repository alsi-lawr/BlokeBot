using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.OAuth;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Users;
using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotAuth(this IServiceCollection services)
    {
        _ = services.AddScoped<BlokeBotPageContextAccessor>();
        _ = services.AddSingleton<WebAuthConfiguration>();
        _ = services.AddTransient<ModeratedChannelLookupService>();
        _ = services.AddSingleton<ModeratorAuthorityService>();
        _ = services.AddSingleton<IModeratorAuthorityService>(static serviceProvider =>
            serviceProvider.GetRequiredService<ModeratorAuthorityService>()
        );
        _ = services.AddTransient<WebAuthService>();
        _ = services.AddTransient<WebOAuthClient>();
        _ = services.AddScoped<AuthSessionService>();
        _ = services.AddSingleton<IAuthorizationHandler, AuthSessionCapabilityHandler>();
        _ = services.AddTransient<UserLookupService>();
        _ = services.AddTransient<ChannelBotOAuthService>();
        _ = services.AddScoped<AuthCookieValidator>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
