using BlokeBot.Identity;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.Admin.Authorization;

public sealed class BotAdminService(IOptions<BlokeBotOptions> options)
{
    public bool IsAdmin(string? login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return false;

        return options.Value.BotAdmins.Any(admin =>
            string.Equals(
                LoginName.Parse(admin).Value,
                LoginName.Parse(login).Value,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }
}
