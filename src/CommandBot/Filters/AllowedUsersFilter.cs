using BlokeBot.Commands;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Options;

public sealed class AllowedUsersFilter(IOptions<AllowedLoginOptions> options) : ITwitchCommandFilter
{
    public ValueTask<bool> AllowAsync(
        TwitchCommandContext context,
        CancellationToken cancellationToken
    )
    {
        var allowed = options.Value.AllowedLogins;
        var isAllowed = allowed.Contains(context.Message.Login, StringComparer.OrdinalIgnoreCase);
        return ValueTask.FromResult(isAllowed);
    }
}
