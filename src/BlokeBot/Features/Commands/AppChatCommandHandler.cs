using Alsi.TwitchBot;

namespace BlokeBot.Features.Commands;

public interface AppChatCommandHandler
{
    Task<bool> TryHandleAsync(
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    );
}
