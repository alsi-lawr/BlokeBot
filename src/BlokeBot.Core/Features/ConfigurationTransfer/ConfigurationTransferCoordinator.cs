using System.Data;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationTransferCoordinator(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CustomCommandConfigurationTransferAdapter customCommands,
    IModeratorAuthorityService moderatorAuthority,
    ConfigurationActivationQueue activationQueue,
    TimeProvider timeProvider,
    ILogger<ConfigurationTransferCoordinator> logger
)
{
    public async Task<ConfigurationImportApplyOutcome> ApplyAsync(
        AuthenticatedSession session,
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection,
        ConfigurationImportActor actor,
        CancellationToken cancellationToken
    )
    {
        var operationId = Guid.NewGuid();
        if (ValidateSelection(document, selection) is { } selectionIssue)
        {
            return new ConfigurationImportApplyOutcome.Invalid(operationId, [selectionIssue]);
        }
        if (!SelectedHostMatches(session, selection.DestinationHostId))
        {
            return new ConfigurationImportApplyOutcome.Rejected(
                operationId,
                "The selected channel changed. Review the import again."
            );
        }
        if (!session.CanManageSelectedHostConfig)
        {
            var authority = await moderatorAuthority.AuthorizeAsync(
                session,
                selection.DestinationHostId,
                cancellationToken
            );
            if (authority is not ModeratorAuthorityOutcome.Granted)
            {
                return new ConfigurationImportApplyOutcome.Rejected(
                    operationId,
                    "Moderator authority could not be confirmed."
                );
            }
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken
            );
            var host = await db.Hosts.SingleOrDefaultAsync(
                x => x.Id == selection.DestinationHostId,
                cancellationToken
            );
            if (host is null)
            {
                return new ConfigurationImportApplyOutcome.Rejected(
                    operationId,
                    "The destination channel no longer exists."
                );
            }

            var issues = new List<ConfigurationValidationIssue>();
            issues.AddRange(
                await customCommands.StageAsync(db, host.Id, document, selection, cancellationToken)
            );
            if (
                Selected(selection, ConfigurationSectionId.Guessing) is { } guessingSelection
                && document.Sections.Guessing is { } guessing
            )
            {
                issues.AddRange(
                    await GuessingConfigurationTransferAdapter.StageAsync(
                        db,
                        host.Id,
                        guessing,
                        guessingSelection,
                        cancellationToken
                    )
                );
            }
            if (
                Selected(selection, ConfigurationSectionId.Points) is { } pointsSelection
                && document.Sections.Points is { } points
            )
            {
                var exists = await db.PointsSettings.AnyAsync(
                    x => x.HostId == host.Id,
                    cancellationToken
                );
                if (!(pointsSelection.Strategy == ImportConflictStrategy.AddMissing && exists))
                {
                    issues.AddRange(
                        await PointsConfigurationTransferAdapter.StageAsync(
                            db,
                            host.Id,
                            points,
                            cancellationToken
                        )
                    );
                }
            }
            if (issues.Count > 0)
            {
                return new ConfigurationImportApplyOutcome.Invalid(operationId, issues);
            }

            var activation = await StageEnablementAsync(
                db,
                host,
                document,
                selection,
                cancellationToken
            );
            var now = timeProvider.GetUtcNow().UtcDateTime;
            _ = db.ConfigurationImportAudits.Add(
                new ConfigurationImportAudit
                {
                    HostId = host.Id,
                    OperationId = operationId,
                    ActorTwitchUserId = actor.TwitchUserId[
                        ..Math.Min(actor.TwitchUserId.Length, 128)
                    ],
                    ActorLogin = actor.Login[..Math.Min(actor.Login.Length, 128)],
                    SourceFormatVersion = document.Version,
                    OccurredAtUtc = now,
                    SummaryJson = AuditSummary(document, selection),
                }
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (activation is not null)
            {
                activationQueue.Wake();
            }

            return new ConfigurationImportApplyOutcome.Applied(
                new(
                    operationId,
                    activation?.Id,
                    selection.Sections.Select(x => x.Section).Distinct().ToArray()
                )
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
                "Configuration import {OperationId} failed before commit.",
                operationId
            );
            return new ConfigurationImportApplyOutcome.Failed(operationId, "persistence");
        }
    }

    private async Task<ConfigurationActivation?> StageEnablementAsync(
        BlokeBotDbContext db,
        BotHost host,
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        if (
            Selected(selection, ConfigurationSectionId.ChannelToolEnablement) is null
            || document.Sections.ChannelToolEnablement is not { } imported
            || selection.EnablementChanges.Count == 0
        )
        {
            return null;
        }
        var importedFlags = ChannelToolEnablementMapper.ToFlags(imported);
        var previous = host.EnabledFeatures;
        var updated = previous;
        foreach (var feature in selection.EnablementChanges)
        {
            updated = importedFlags.Contains(feature) ? updated | feature : updated & ~feature;
        }
        if (updated == previous)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await HostFeatureTransitionStager.StageAsync(db, host, updated, now, cancellationToken);
        var enabled = updated & ~previous;
        var disabled = previous & ~updated;
        var pending = await db.ConfigurationActivations.SingleOrDefaultAsync(
            x =>
                x.HostId == host.Id
                && (
                    x.Status == ConfigurationActivationStatus.Pending
                    || x.Status == ConfigurationActivationStatus.Processing
                ),
            cancellationToken
        );
        if (pending is null)
        {
            pending = new ConfigurationActivation
            {
                Id = Guid.NewGuid(),
                HostId = host.Id,
                Status = ConfigurationActivationStatus.Pending,
                CreatedAtUtc = now,
            };
            _ = db.ConfigurationActivations.Add(pending);
        }
        var queuedEnabled = pending.EnabledChanges;
        var queuedDisabled = pending.DisabledChanges;
        pending.EnabledChanges = (queuedEnabled & ~disabled) | (enabled & ~queuedDisabled);
        pending.DisabledChanges = (queuedDisabled & ~enabled) | (disabled & ~queuedEnabled);
        pending.Status = ConfigurationActivationStatus.Pending;
        pending.Revision++;
        pending.UpdatedAtUtc = now;
        pending.FailureCode = null;
        pending.CompletedAtUtc = null;
        return pending;
    }
}

public abstract record ConfigurationImportApplyOutcome
{
    private ConfigurationImportApplyOutcome() { }

    public sealed record Applied(ConfigurationImportApplied Result)
        : ConfigurationImportApplyOutcome;

    public sealed record Invalid(
        Guid OperationId,
        IReadOnlyList<ConfigurationValidationIssue> Issues
    ) : ConfigurationImportApplyOutcome;

    public sealed record Rejected(Guid OperationId, string Message)
        : ConfigurationImportApplyOutcome;

    public sealed record Failed(Guid OperationId, string FailureCode)
        : ConfigurationImportApplyOutcome;
}
