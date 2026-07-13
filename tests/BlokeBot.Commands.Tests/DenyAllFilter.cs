using BlokeBot.Commands;

namespace BlokeBot.Commands.Tests;

internal sealed class DenyAllFilter : ITwitchCommandFilter
{
    public ValueTask<bool> AllowAsync(
        TwitchCommandContext context,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.FromResult(false);
    }
}
