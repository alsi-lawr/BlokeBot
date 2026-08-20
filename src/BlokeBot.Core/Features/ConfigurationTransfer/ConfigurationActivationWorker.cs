using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed class ConfigurationActivationWorker(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ConfigurationActivationQueue queue,
    ConfigurationActivationDispatcher dispatcher,
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
                x.Status == ConfigurationActivationStatus.Pending
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
            await dispatcher.ActivateAsync(
                claim.HostId,
                claim.Enabled,
                claim.Disabled,
                cancellationToken
            );
            await SetOutcomeAsync(
                claim,
                ConfigurationActivationStatus.Complete,
                null,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
                exception.GetType().Name,
                cancellationToken
            );
        }
    }

    private async Task SetOutcomeAsync(
        ActivationClaim claim,
        ConfigurationActivationStatus status,
        string? failureCode,
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
                        .SetProperty(x => x.FailureCode, failureCode)
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
