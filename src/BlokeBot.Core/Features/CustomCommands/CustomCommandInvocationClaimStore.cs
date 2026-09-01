using BlokeBot.Persistence;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandInvocationClaimStore(TimeProvider clock)
{
    internal const int CleanupBatchSize = 100;
    internal static readonly TimeSpan StreamClaimRetention = TimeSpan.FromDays(7);

    public async Task<CustomCommandInvocationClaimOutcome> TryClaimAsync(
        BlokeBotDbContext db,
        CustomCommandInvocationClaimRequest request,
        CancellationToken ct
    )
    {
        var streamId = request.Scope switch
        {
            CustomCommandInvocationScope.OncePerStream scope => scope.TwitchStreamId,
            CustomCommandInvocationScope.OncePerStreamPerUser scope => scope.TwitchStreamId,
            _ => null,
        };
        if (streamId is { } currentStreamId)
        {
            await CleanupExpiredStreamClaimsAsync(db, currentStreamId, ct);
        }

        var viewerId = request.Scope switch
        {
            CustomCommandInvocationScope.OncePerUser scope => scope.TwitchUserId,
            CustomCommandInvocationScope.OncePerStreamPerUser scope => scope.TwitchUserId,
            _ => null,
        };
        var changed = await MainDatabaseStatements.TryClaimCustomCommandInvocationAsync(
            db,
            request.HostId,
            request.CommandId,
            viewerId,
            streamId,
            clock.GetUtcNow().UtcDateTime,
            ct
        );
        return changed == 1
            ? new CustomCommandInvocationClaimOutcome.Claimed()
            : new CustomCommandInvocationClaimOutcome.AlreadyUsed();
    }

    private async Task CleanupExpiredStreamClaimsAsync(
        BlokeBotDbContext db,
        string currentStreamId,
        CancellationToken ct
    )
    {
        var cutoff = clock.GetUtcNow().Subtract(StreamClaimRetention).UtcDateTime;
        _ = await MainDatabaseStatements.DeleteExpiredCustomCommandClaimsAsync(
            db,
            currentStreamId,
            cutoff,
            CleanupBatchSize,
            ct
        );
    }
}

public sealed record CustomCommandInvocationClaimRequest(
    int HostId,
    int CommandId,
    CustomCommandInvocationScope Scope
);

public abstract record CustomCommandInvocationScope
{
    private CustomCommandInvocationScope() { }

    public sealed record OncePerStream(string TwitchStreamId) : CustomCommandInvocationScope;

    public sealed record OncePerUser(string TwitchUserId) : CustomCommandInvocationScope;

    public sealed record OncePerStreamPerUser(string TwitchStreamId, string TwitchUserId)
        : CustomCommandInvocationScope;
}

public abstract record CustomCommandInvocationClaimOutcome
{
    private CustomCommandInvocationClaimOutcome() { }

    public sealed record Claimed : CustomCommandInvocationClaimOutcome;

    public sealed record AlreadyUsed : CustomCommandInvocationClaimOutcome;
}
