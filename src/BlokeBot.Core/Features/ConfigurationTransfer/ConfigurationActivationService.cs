using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed class ConfigurationActivationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ConfigurationActivationQueue queue,
    TimeProvider timeProvider
)
{
    public async Task<ConfigurationActivationView?> LoadAsync(
        int hostId,
        Guid activationId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .ConfigurationActivations.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Id == activationId)
            .Select(x => new ConfigurationActivationView(
                x.Id,
                x.Status,
                x.AttemptCount,
                x.FailureCode
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> RetryAsync(
        int hostId,
        Guid activationId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var retried = await db
            .ConfigurationActivations.Where(x =>
                x.HostId == hostId
                && x.Id == activationId
                && x.Status == ConfigurationActivationStatus.Failed
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Status, ConfigurationActivationStatus.Pending)
                        .SetProperty(x => x.FailureCode, (string?)null)
                        .SetProperty(x => x.Revision, x => x.Revision + 1)
                        .SetProperty(x => x.UpdatedAtUtc, timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken
            );
        if (retried > 0)
        {
            queue.Wake();
        }

        return retried > 0;
    }
}

public sealed record ConfigurationActivationView(
    Guid Id,
    ConfigurationActivationStatus Status,
    int AttemptCount,
    string? FailureCode
);
