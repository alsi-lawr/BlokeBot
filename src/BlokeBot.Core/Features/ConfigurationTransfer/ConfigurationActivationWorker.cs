using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed class ConfigurationActivationWorker(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ConfigurationActivationQueue queue,
    HostFeatureActivationAuthority activation,
    TimeProvider timeProvider,
    ILogger<ConfigurationActivationWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan _claimLease = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            while (await ClaimAsync(stoppingToken) is { } claim)
            {
                await ProcessAsync(claim, stoppingToken);
            }
            await queue.WaitAsync(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task<ActivationClaim?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var staleBefore = now - _claimLease;
        var candidate = await db
            .ConfigurationActivations.AsNoTracking()
            .Where(x =>
                (
                    x.Status == ConfigurationActivationStatus.Pending
                    && !db.ConfigurationActivations.Any(other =>
                        other.HostId == x.HostId
                        && other.Id != x.Id
                        && other.Status == ConfigurationActivationStatus.Processing
                        && other.UpdatedAtUtc > staleBefore
                    )
                )
                || (
                    x.Status == ConfigurationActivationStatus.Processing
                    && x.UpdatedAtUtc <= staleBefore
                )
            )
            .OrderBy(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        var claimed = await db
            .ConfigurationActivations.Where(x =>
                x.Id == candidate.Id
                && x.Revision == candidate.Revision
                && (
                    x.Status == ConfigurationActivationStatus.Pending
                    || (
                        x.Status == ConfigurationActivationStatus.Processing
                        && x.UpdatedAtUtc <= staleBefore
                    )
                )
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Status, ConfigurationActivationStatus.Processing)
                        .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                        .SetProperty(x => x.Revision, x => x.Revision + 1)
                        .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken
            );
        return claimed == 0
            ? null
            : new(
                candidate.Id,
                candidate.HostId,
                candidate.EnabledChanges,
                candidate.DisabledChanges,
                candidate.Revision + 1
            );
    }

    private async Task ProcessAsync(ActivationClaim claim, CancellationToken cancellationToken)
    {
        try
        {
            var result = await activation.ApplyAsync(
                claim.HostId,
                claim.Enabled,
                claim.Disabled,
                cancellationToken
            );
            switch (result)
            {
                case HostFeatureActivationResult.Complete:
                    await SetOutcomeAsync(
                        claim,
                        ConfigurationActivationStatus.Complete,
                        null,
                        cancellationToken
                    );
                    break;
                case HostFeatureActivationResult.Failed failed:
                    await SetOutcomeAsync(
                        claim,
                        ConfigurationActivationStatus.Failed,
                        [failed.Issue],
                        cancellationToken
                    );
                    break;
                case HostFeatureActivationResult.ManualFollowUp manual:
                    await SetOutcomeAsync(
                        claim,
                        ConfigurationActivationStatus.ManualFollowUp,
                        manual.Issues,
                        cancellationToken
                    );
                    break;
                case HostFeatureActivationResult.Canceled canceled:
                    await SetOutcomeAsync(
                        claim,
                        ConfigurationActivationStatus.Pending,
                        [canceled.Issue],
                        CancellationToken.None
                    );
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Configuration activation {ActivationId} failed for host {HostId}.",
                claim.Id,
                claim.HostId
            );
            await SetOutcomeAsync(
                claim,
                ConfigurationActivationStatus.Failed,
                [
                    new(
                        HostFeatureActivationAuthority.AutomaticWorkFailureCode,
                        "The saved feature setting could not be activated automatically. Retry automatic activation."
                    ),
                ],
                CancellationToken.None
            );
        }
    }

    private async Task SetOutcomeAsync(
        ActivationClaim claim,
        ConfigurationActivationStatus status,
        IReadOnlyList<HostFeatureActivationIssue>? issues,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        _ = await db
            .ConfigurationActivations.Where(x => x.Id == claim.Id && x.Revision == claim.Revision)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Status, status)
                        .SetProperty(
                            x => x.IssuesJson,
                            issues == null ? null : JsonSerializer.Serialize(issues)
                        )
                        .SetProperty(x => x.UpdatedAtUtc, now)
                        .SetProperty(
                            x => x.CompletedAtUtc,
                            status == ConfigurationActivationStatus.Complete ? now : null
                        ),
                cancellationToken
            );
    }

    private sealed record ActivationClaim(
        Guid Id,
        int HostId,
        HostFeatureFlags Enabled,
        HostFeatureFlags Disabled,
        long Revision
    );
}
