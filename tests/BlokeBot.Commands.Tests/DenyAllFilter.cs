using BlokeBot.Commands;

namespace BlokeBot.Commands.Tests;

internal sealed class DenyAllFilter : IChatCommandFilter
{
    public ValueTask<bool> AllowAsync(
        ChatCommandContext context,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.FromResult(false);
    }
}
