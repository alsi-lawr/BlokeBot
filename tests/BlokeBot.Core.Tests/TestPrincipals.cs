using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Hosts;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BlokeBot.Core.Tests;

internal static class TestPrincipals
{
    public static ClaimsPrincipal BlokeBotUser(
        string login,
        AuthRole? role = null,
        string? roleClaim = null,
        bool canCreateHost = false,
        bool isBotAdmin = false,
        bool isBotAccount = false,
        IReadOnlyList<BotHostChoice>? availableHosts = null,
        IReadOnlyList<string>? availableHostClaims = null,
        BotHostChoice? selectedHost = null,
        string? selectedHostClaim = null
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
        };

        if (roleClaim is not null)
        {
            claims.Add(new Claim(AuthClaims.Role, roleClaim));
        }
        else if (role is not null)
        {
            claims.Add(new Claim(AuthClaims.Role, AuthRoleCodec.Encode(role.Value)));
        }

        if (availableHosts is not null)
        {
            claims.AddRange(
                availableHosts.Select(static host => new Claim(
                    BotHostClaims.AvailableHost,
                    BotHostClaimCodec.Encode(host)
                ))
            );
        }

        if (availableHostClaims is not null)
        {
            claims.AddRange(
                availableHostClaims.Select(static value => new Claim(
                    BotHostClaims.AvailableHost,
                    value
                ))
            );
        }

        if (selectedHostClaim is not null)
        {
            claims.Add(new Claim(BotHostClaims.SelectedHost, selectedHostClaim));
        }
        else if (selectedHost is not null)
        {
            claims.Add(
                new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(selectedHost))
            );
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
        );
    }
}
