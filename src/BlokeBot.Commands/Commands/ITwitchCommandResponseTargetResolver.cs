namespace BlokeBot.Commands;

public interface ITwitchCommandResponseTargetResolver
{
    ValueTask<TwitchCommandResponseTarget> ResolveAsync(
        TwitchCommandContext context,
        CancellationToken cancellationToken
    );
}
