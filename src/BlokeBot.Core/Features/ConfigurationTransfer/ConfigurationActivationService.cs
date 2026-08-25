using System.Text.Json;
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
        var activation = await db
            .ConfigurationActivations.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Id == activationId)
            .SingleOrDefaultAsync(cancellationToken);
        return activation is null
            ? null
            : new(
                activation.Id,
                activation.Status,
                activation.AttemptCount,
                activation.IssuesJson is null
                    ? []
                    : JsonSerializer.Deserialize<ConfigurationActivationIssue[]>(
                        activation.IssuesJson
                    ) ?? []
            );
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
                && (
                    x.Status == ConfigurationActivationStatus.Failed
                    || x.Status == ConfigurationActivationStatus.ManualFollowUp
                )
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Status, ConfigurationActivationStatus.Pending)
                        .SetProperty(x => x.IssuesJson, (string?)null)
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
    IReadOnlyList<ConfigurationActivationIssue> Issues
);

public sealed record ConfigurationActivationIssue(string Code, string Reason);
