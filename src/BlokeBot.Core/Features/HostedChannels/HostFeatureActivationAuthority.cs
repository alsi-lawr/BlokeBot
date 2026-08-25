using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.HostedChannels;

public enum HostFeatureActivationState
{
    Disabled,
    Enabled,
}

public sealed record HostFeatureActivationChange(
    int HostId,
    HostFeatureFlags Feature,
    HostFeatureActivationState State
);

public interface IHostFeatureActivationObserver
{
    ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    );
}

public abstract record HostFeatureAutomaticWorkResult
{
    private HostFeatureAutomaticWorkResult() { }

    public sealed record Complete : HostFeatureAutomaticWorkResult;

    public sealed record Failed(HostFeatureActivationIssue Issue) : HostFeatureAutomaticWorkResult;

    public sealed record ManualFollowUp(HostFeatureManualFollowUp FollowUp)
        : HostFeatureAutomaticWorkResult;
}

public sealed record HostFeatureActivationIssue(string Code, string Reason);

public sealed record HostFeatureManualFollowUp(
    string Code,
    string Reason,
    string StableKey,
    string AlertTitle,
    string AlertMessage,
    string? LinkPath
);

public abstract record HostFeatureActivationResult
{
    private HostFeatureActivationResult() { }

    public sealed record Complete : HostFeatureActivationResult;

    public sealed record Failed(HostFeatureActivationIssue Issue) : HostFeatureActivationResult;

    public sealed record Canceled(HostFeatureActivationIssue Issue) : HostFeatureActivationResult;

    public sealed record ManualFollowUp(IReadOnlyList<HostFeatureActivationIssue> Issues)
        : HostFeatureActivationResult;
}

public sealed class HostFeatureActivationAuthority(
    IEnumerable<IHostFeatureActivationObserver> observers,
    HostedChannelChangeNotifier changes,
    DurableAlertService alerts,
    ILogger<HostFeatureActivationAuthority> logger
)
{
    public const string AutomaticWorkFailureCode = "automatic-work-failed";
    public const string CancellationCode = "automatic-work-canceled";
    public const string NotificationFailureCode = "feature-change-notification-failed";
    public const string ManualFollowUpAlertFailureCode = "manual-follow-up-alert-failed";
    public const string AlertSource = "feature-activation";

    private readonly IReadOnlyList<IHostFeatureActivationObserver> _observers = observers.ToArray();

    public async Task<HostFeatureActivationResult> ApplyAsync(
        int hostId,
        HostFeatureFlags enabled,
        HostFeatureFlags disabled,
        CancellationToken cancellationToken
    )
    {
        var manualFollowUps = new List<HostFeatureManualFollowUp>();
        foreach (var feature in ChangedFeatures(enabled, disabled))
        {
            var change = new HostFeatureActivationChange(
                hostId,
                feature,
                enabled.Contains(feature)
                    ? HostFeatureActivationState.Enabled
                    : HostFeatureActivationState.Disabled
            );
            foreach (var observer in _observers)
            {
                HostFeatureAutomaticWorkResult result;
                try
                {
                    result = await observer.ApplyAsync(change, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new HostFeatureActivationResult.Canceled(
                        new(CancellationCode, CancellationReason(change.Feature))
                    );
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Automatic feature work {ObserverType} failed for host {HostId} and feature {Feature}.",
                        observer.GetType().Name,
                        hostId,
                        feature
                    );
                    return new HostFeatureActivationResult.Failed(
                        new(AutomaticWorkFailureCode, FailureReason(change.Feature))
                    );
                }

                switch (result)
                {
                    case HostFeatureAutomaticWorkResult.Complete:
                        break;
                    case HostFeatureAutomaticWorkResult.Failed failed:
                        return new HostFeatureActivationResult.Failed(failed.Issue);
                    case HostFeatureAutomaticWorkResult.ManualFollowUp manual:
                        if (!manualFollowUps.Contains(manual.FollowUp))
                        {
                            manualFollowUps.Add(manual.FollowUp);
                        }
                        break;
                }
            }
        }

        try
        {
            _ = await changes.NotifyChangedAsync(cancellationToken);
            return manualFollowUps.Count == 0
                ? new HostFeatureActivationResult.Complete()
                : await ReportManualFollowUpsAsync(hostId, manualFollowUps, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new HostFeatureActivationResult.Canceled(
                new(
                    CancellationCode,
                    "Automatic feature activation was interrupted and will be retried."
                )
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Publishing the feature change failed for host {HostId}.",
                hostId
            );
            return new HostFeatureActivationResult.Failed(
                new(
                    NotificationFailureCode,
                    "The feature setting was saved, but BlokeBot could not publish the channel update. Retry automatic activation."
                )
            );
        }
    }

    private async Task<HostFeatureActivationResult> ReportManualFollowUpsAsync(
        int hostId,
        IReadOnlyList<HostFeatureManualFollowUp> followUps,
        CancellationToken cancellationToken
    )
    {
        try
        {
            foreach (var followUp in followUps)
            {
                _ = await alerts
                    .Create(
                        hostId,
                        DurableAlertSeverity.Warning,
                        AlertSource,
                        followUp.StableKey,
                        followUp.AlertTitle,
                        followUp.AlertMessage,
                        followUp.LinkPath
                    )
                    .ExecuteAsync(cancellationToken);
            }
            return new HostFeatureActivationResult.ManualFollowUp(
                followUps
                    .Select(followUp => new HostFeatureActivationIssue(
                        followUp.Code,
                        followUp.Reason
                    ))
                    .ToArray()
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new HostFeatureActivationResult.Canceled(
                new(
                    CancellationCode,
                    "Manual follow-up reporting was interrupted and will be retried."
                )
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Creating a manual follow-up alert failed for host {HostId}.",
                hostId
            );
            return new HostFeatureActivationResult.Failed(
                new(
                    ManualFollowUpAlertFailureCode,
                    "Automatic activation found a required manual step, but BlokeBot could not create its alert. Retry automatic activation."
                )
            );
        }
    }

    private static IEnumerable<HostFeatureFlags> ChangedFeatures(
        HostFeatureFlags enabled,
        HostFeatureFlags disabled
    ) =>
        HostFeatureCatalog.Features.Where(feature =>
            enabled.Contains(feature) || disabled.Contains(feature)
        );

    private static string FailureReason(HostFeatureFlags feature) =>
        $"The {FeatureName(feature)} setting was saved, but its automatic activation did not finish. Retry automatic activation.";

    private static string CancellationReason(HostFeatureFlags feature) =>
        $"Automatic activation for {FeatureName(feature)} was interrupted and will be retried.";

    private static string FeatureName(HostFeatureFlags feature) =>
        HostFeatureCatalog.Cards(feature).Single(card => card.Feature == feature).Name;
}
