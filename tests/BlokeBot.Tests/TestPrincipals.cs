using System.Security.Claims;
using BlokeBot.Auth.Sessions;
using BlokeBot.Hosts;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BlokeBot.Tests;

internal static class TestPrincipals
{
    public static ClaimsPrincipal BlokeBotUser(
        string login,
        string role,
        bool canCreateHost = false,
        bool isBotAdmin = false,
        bool isBotAccount = false,
        IReadOnlyList<BotHostChoice>? availableHosts = null,
        string? selectedHostId = null
    )
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"{login}-id"),
            new(ClaimTypes.Name, login),
            new(AuthClaims.CanCreateHost, canCreateHost ? "true" : "false"),
            new(AuthClaims.Login, login),
            new(AuthClaims.IsBotAdmin, isBotAdmin ? "true" : "false"),
            new(AuthClaims.IsBotAccount, isBotAccount ? "true" : "false"),
            new(AuthClaims.Role, role),
        };

        if (availableHosts is not null)
        {
            claims.AddRange(
                availableHosts.Select(host => new Claim(
                    BotHostClaims.AvailableHost,
                    BotHostClaimCodec.Encode(host)
                ))
            );
        }

        if (selectedHostId is not null)
            claims.Add(new Claim(BotHostClaims.SelectedHostId, selectedHostId));

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
        );
    }
}
