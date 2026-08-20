using BlokeBot.Core.Auth.OAuth;
using BlokeBot.Core.Auth.Sessions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotApplication
{
    private static void AddAuthentication(WebApplicationBuilder builder)
    {
        _ = builder
            .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                var webAuthOptions =
                    builder.Configuration.GetSection("TwitchWebAuth").Get<WebAuthOptions>()
                    ?? new WebAuthOptions();

                options.AccessDeniedPath = "/auth/login";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Name = string.IsNullOrWhiteSpace(webAuthOptions.CookieName)
                    ? "BlokeBot.Auth"
                    : webAuthOptions.CookieName;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.LoginPath = "/auth/login";
                options.LogoutPath = "/auth/logout";
                options.SlidingExpiration = false;
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = context =>
                        context
                            .HttpContext.RequestServices.GetRequiredService<AuthCookieValidator>()
                            .ValidateAsync(context),
                };
            });
        _ = builder.Services.AddAuthorization(options =>
        {
            AddPolicy(options, "Operator", AuthSessionCapability.Operator);
            AddPolicy(options, "HostSelected", AuthSessionCapability.HostSelected);
            AddPolicy(options, "BotAdmin", AuthSessionCapability.BotAdmin);
        });
    }

    private static void AddPolicy(
        AuthorizationOptions options,
        string name,
        AuthSessionCapability capability
    ) =>
        options.AddPolicy(
            name,
            policy =>
                policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new AuthSessionCapabilityRequirement(capability))
        );
}
