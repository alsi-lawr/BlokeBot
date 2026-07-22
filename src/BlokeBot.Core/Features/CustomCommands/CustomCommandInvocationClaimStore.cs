using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

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
        var changed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT OR IGNORE INTO custom_command_invocation_claims
                (HostId, CustomCommandId, TwitchUserId, TwitchStreamId, ClaimedAtUtc)
            VALUES
                ({request.HostId}, {request.CommandId}, {viewerId}, {streamId}, {clock.GetUtcNow().UtcDateTime});
            """,
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
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM custom_command_invocation_claims
            WHERE Id IN (
                SELECT Id
                FROM custom_command_invocation_claims
                WHERE TwitchStreamId IS NOT NULL
                  AND TwitchStreamId <> {currentStreamId}
                  AND ClaimedAtUtc < {cutoff}
                ORDER BY ClaimedAtUtc, Id
                LIMIT {CleanupBatchSize}
            );
            """,
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
