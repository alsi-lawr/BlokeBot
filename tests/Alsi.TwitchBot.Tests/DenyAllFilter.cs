using Alsi.TwitchBot;

namespace Alsi.TwitchBot.Tests;

internal sealed class DenyAllFilter : ITwitchCommandFilter
{
    public ValueTask<bool> AllowAsync(
        TwitchCommandContext context,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(false);
}
