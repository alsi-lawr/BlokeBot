using System.Security.Claims;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Hosts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BlokeBot.Auth.Sessions;

internal sealed class AuthSessionService(
    BotAdminService admins,
    TwitchBotSettings botSettings
)
{
    public async Task SignInAsync(
        HttpContext context,
        AuthenticatedUser user,
        int? preferredHostId = null
    )
    {
        var isBotAccount = IsConfiguredBotAccount(user.Login);
        var hosts = isBotAccount ? [] : user.Hosts;
        var selectedHost = SelectInitialHost(
            hosts,
            preferredHostId,
            user.Login,
            user.CanCreateHost
        );
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
            !isBotAccount && user.CanCreateHost
        );

        await SignInAsync(context, principal);
    }

    public async Task SignInHostSelectionAsync(
        HttpContext context,
        IReadOnlyList<BotHostChoice> hosts,
        BotHostChoice? selectedHost,
        bool isBotAdmin,
        string? adminEditingLogin,
        BotHostChoice? adminReturnHost = null
    )
    {
        var current = AuthenticatedSession.FromPrincipal(context.User);
        var principal = CreatePrincipal(
            current.UserId,
            string.IsNullOrWhiteSpace(current.DisplayName)
                ? selectedHost?.DisplayName ?? current.Login
                : current.DisplayName,
            current.Login,
            current.ProfileImageUrl,
            hosts,
            selectedHost,
            isBotAdmin,
            current.IsBotAccount,
            adminEditingLogin,
            adminReturnHost,
            current.CanCreateHost
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
        await SignInHostSelectionAsync(
            context,
            hosts,
            selectedHost,
            isBotAdmin,
            adminEditingLogin,
            adminReturnHost
        );
    }

    public async Task SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

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
        {
            claims.Add(new(AuthClaims.Role, AuthRoleCodec.Encode(AuthRole.Bot)));
        }

        if (selectedHost is not null)
        {
            claims.AddRange([
                new(AuthClaims.Role, AuthRoleCodec.Encode(selectedHost.Role)),
                new(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(selectedHost)),
            ]);
        }

        if (!string.IsNullOrWhiteSpace(profileImageUrl))
        {
            claims.Add(new Claim(AuthClaims.ProfileImageUrl, profileImageUrl));
        }

        if (!string.IsNullOrWhiteSpace(adminEditingLogin))
        {
            claims.Add(new Claim(BotHostClaims.AdminEditingLogin, adminEditingLogin));
        }

        if (adminReturnHost is not null)
        {
            claims.Add(
                new Claim(BotHostClaims.AdminReturnHost, BotHostClaimCodec.Encode(adminReturnHost))
            );
        }

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

    public bool IsConfiguredBotAccount(string login)
    {
        return !string.IsNullOrWhiteSpace(botSettings.Identity.BotUsername)
        && string.Equals(
            TwitchLogin.Normalize(login),
            botSettings.Identity.BotUsername,
            StringComparison.Ordinal
        );
    }

    private static BotHostChoice? SelectInitialHost(
        IReadOnlyList<BotHostChoice> hosts,
        int? preferredHostId,
        string login,
        bool canCreateHost
    )
    {
        if (hosts.Count == 0)
        {
            return null;
        }

        if (preferredHostId is { } hostId)
        {
            var preferred = hosts.FirstOrDefault(host => host.Id == hostId);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        var ownHost = hosts.FirstOrDefault(host =>
            host.Role == AuthRole.Streamer
            && string.Equals(host.Login, login, StringComparison.OrdinalIgnoreCase)
        );
        if (ownHost is not null)
        {
            return ownHost;
        }

        return canCreateHost ? null : hosts[0];
    }

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
