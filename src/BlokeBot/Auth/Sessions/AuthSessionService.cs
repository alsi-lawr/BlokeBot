using System.Security.Claims;
using Alsi.TwitchBot;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Hosts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace BlokeBot.Auth.Sessions;

internal sealed class AuthSessionService(
    BotAdminService admins,
    IOptions<TwitchBotOptions> botOptions
)
{
    public string? AdminEditingLogin(ClaimsPrincipal user) =>
        user.FindFirstValue(BotHostClaims.AdminEditingLogin);

    public BotHostChoice? AdminReturnHost(ClaimsPrincipal user)
    {
        var encoded = user.FindFirstValue(BotHostClaims.AdminReturnHost);
        return string.IsNullOrWhiteSpace(encoded) ? null : BotHostClaimCodec.Decode(encoded);
    }

    public bool IsBotAdmin(ClaimsPrincipal user) =>
        string.Equals(
            user.FindFirstValue(AuthClaims.IsBotAdmin),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    public bool IsBotAccount(ClaimsPrincipal user) =>
        string.Equals(
            user.FindFirstValue(AuthClaims.IsBotAccount),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    public string Login(ClaimsPrincipal user) =>
        user.FindFirstValue(AuthClaims.Login) ?? string.Empty;

    public async Task SignInAsync(
        HttpContext context,
        AuthenticatedUser user,
        int? preferredHostId = null
    )
    {
        var isBotAccount = IsConfiguredBotAccount(user.Login);
        var hosts = isBotAccount ? [] : user.Hosts;
        var selectedHost =
            hosts.Count == 0 ? null
            : preferredHostId is { } hostId
                ? hosts.FirstOrDefault(host => host.Id == hostId) ?? hosts[0]
            : hosts[0];
        var isBotAdmin = isBotAccount || admins.IsAdmin(user.Login);
        var principal = CreatePrincipal(
            user.Id,
            user.DisplayName,
            user.Login,
            user.ProfileImageUrl,
            hosts,
            selectedHost,
            isBotAdmin,
            isBotAccount,
            null,
            null,
            isBotAccount ? false : user.CanCreateHost
        );

        await SignInAsync(context, principal);
    }

    public async Task SignInHostAsync(
        HttpContext context,
        IReadOnlyList<BotHostChoice> hosts,
        BotHostChoice selectedHost,
        bool isBotAdmin,
        string? adminEditingLogin,
        BotHostChoice? adminReturnHost = null
    )
    {
        var principal = CreatePrincipal(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            context.User.Identity?.Name ?? selectedHost.DisplayName,
            Login(context.User),
            context.User.FindFirstValue(AuthClaims.ProfileImageUrl),
            hosts,
            selectedHost,
            isBotAdmin,
            IsBotAccount(context.User),
            adminEditingLogin,
            adminReturnHost,
            CanCreateHost(context.User)
        );

        await SignInAsync(context, principal);
    }

    public async Task SignOutAsync(HttpContext context) =>
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    public bool CanCreateHost(ClaimsPrincipal user) =>
        string.Equals(
            user.FindFirstValue(AuthClaims.CanCreateHost),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    private static ClaimsPrincipal CreatePrincipal(
        string userId,
        string displayName,
        string login,
        string? profileImageUrl,
        IReadOnlyList<BotHostChoice> hosts,
        BotHostChoice? selectedHost,
        bool isBotAdmin,
        bool isBotAccount,
        string? adminEditingLogin,
        BotHostChoice? adminReturnHost,
        bool canCreateHost
    )
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, displayName),
            new(AuthClaims.CanCreateHost, canCreateHost ? "true" : "false"),
            new(AuthClaims.Login, login),
            new(AuthClaims.IsBotAdmin, isBotAdmin ? "true" : "false"),
            new(AuthClaims.IsBotAccount, isBotAccount ? "true" : "false"),
        };

        if (isBotAccount)
            claims.Add(new(AuthClaims.Role, AuthRole.Bot));

        if (selectedHost is not null)
        {
            claims.AddRange([
                new(AuthClaims.Role, selectedHost.Role),
                new(BotHostClaims.SelectedHostId, selectedHost.Id.ToString()),
                new(BotHostClaims.SelectedHostLogin, selectedHost.Login),
                new(BotHostClaims.SelectedHostRole, selectedHost.Role),
            ]);
        }

        if (!string.IsNullOrWhiteSpace(profileImageUrl))
            claims.Add(new Claim(AuthClaims.ProfileImageUrl, profileImageUrl));

        if (!string.IsNullOrWhiteSpace(adminEditingLogin))
            claims.Add(new Claim(BotHostClaims.AdminEditingLogin, adminEditingLogin));

        if (adminReturnHost is not null)
            claims.Add(
                new Claim(BotHostClaims.AdminReturnHost, BotHostClaimCodec.Encode(adminReturnHost))
            );

        claims.AddRange(
            hosts.Select(host => new Claim(
                BotHostClaims.AvailableHost,
                BotHostClaimCodec.Encode(host)
            ))
        );
        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
        );
    }

    public bool IsConfiguredBotAccount(string login) =>
        !string.IsNullOrWhiteSpace(botOptions.Value.Identity.BotUsername)
        && string.Equals(
            NormalizeLogin(login),
            NormalizeLogin(botOptions.Value.Identity.BotUsername),
            StringComparison.Ordinal
        );

    private static string NormalizeLogin(string value) =>
        value.Trim().TrimStart('#').ToLowerInvariant();

    private static async Task SignInAsync(HttpContext context, ClaimsPrincipal principal)
    {
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                AllowRefresh = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
                IsPersistent = false,
            }
        );
    }
}
