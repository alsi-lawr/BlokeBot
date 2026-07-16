using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.Admin.Authorization;

public sealed class BotAdminService(BotAdminSettings settings)
{
    public bool IsAdmin(string? login)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return false;
        }

        return settings.BotAdmins.Contains(LoginName.Parse(login));
    }
}
