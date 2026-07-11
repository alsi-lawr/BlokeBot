using System.Collections.Immutable;
using BlokeBot.Identity;

namespace BlokeBot.Features.Admin.Authorization;

public sealed record BotAdminSettings
{
    public required ImmutableHashSet<LoginName> BotAdmins { get; init; }

    public static BotAdminSettings FromOptions(BlokeBotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new BotAdminSettings
        {
            BotAdmins = (options.BotAdmins ?? [])
                .Select(LoginName.Parse)
                .Where(login => !login.IsEmpty)
                .ToImmutableHashSet(),
        };
    }
}
