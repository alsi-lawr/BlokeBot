using System.Collections.Concurrent;
using System.Text.Json;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Predictions;

public sealed class PredictionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostBroadcasterTokenStatusProvider broadcasters,
    HelixClient helix,
    BotSettings settings,
    EventBus<AppEventKind> events,
    DurableAlertService alerts,
    ILogger<PredictionService> logger,
    NativeTwitchFeatureGate nativeTwitch
) : IPredictionEventObserver, IPredictionDashboardOperations
{
    private const int _resultsToKeep = 100;
    private const string _notReadyMessage = "Reconnect the selected channel's Twitch integration.";
    private const string _ineligibleMessage =
        "Twitch Predictions are available only to Affiliate or Partner broadcasters.";
    private readonly ConcurrentDictionary<int, PendingProgress> _pendingProgress = new();
    private readonly ConcurrentDictionary<
        int,
        TaskCompletionSource<PredictionProgressFlushDecision>
    > _progressFlushObservers = new();

    internal PredictionService(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        IHostBroadcasterTokenStatusProvider broadcasters,
        HelixClient helix,
        BotSettings settings,
        EventBus<AppEventKind> events,
        DurableAlertService alerts,
        ILogger<PredictionService> logger,
        NativeTwitchFeatureGate nativeTwitch,
        TimeProvider timeProvider
    )
        : this(dbFactory, broadcasters, helix, settings, events, alerts, logger, nativeTwitch)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ProgressTimeProvider = timeProvider;
    }

    internal TimeProvider ProgressTimeProvider { get; } = TimeProvider.System;

    internal Task<PredictionProgressFlushDecision> ObserveNextProgressFlushAsync(int hostId)
    {
        var observer = new TaskCompletionSource<PredictionProgressFlushDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        return !_progressFlushObservers.TryAdd(hostId, observer)
            ? throw new InvalidOperationException(
                $"Prediction progress for host {hostId} already has a flush observer."
            )
            : observer.Task;
    }

    public async Task<PredictionDashboardState> LoadAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return new(new PredictionAuthorizationReadiness.Disabled(), null, [], []);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var readiness = await ReadinessAsync(hostId, cancellationToken);
        var active = await db
            .TwitchPredictions.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && (
                    x.Status == TwitchPredictionStatus.Active
                    || x.Status == TwitchPredictionStatus.Locked
                )
            )
            .SingleOrDefaultAsync(cancellationToken);
        var templates = await db
            .TwitchPredictionTemplates.AsNoTracking()
            .Include(x => x.Outcomes)
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var results = await db
            .TwitchPredictions.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && x.Status != TwitchPredictionStatus.Active
                && x.Status != TwitchPredictionStatus.Locked
            )
            .OrderByDescending(x => x.EndedAtUtc)
            .Take(_resultsToKeep)
            .ToArrayAsync(cancellationToken);
        return new(
            readiness,
            active is null ? null : View(active),
            templates.Select(View).ToArray(),
            results.Select(View).ToArray()
        );
    }

    public async Task<PredictionOperationOutcome> SaveTemplateAsync(
        int hostId,
        PredictionTemplateDraft draft,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return Disabled();
        }
        if (draft.Validate() is PredictionTemplateValidationOutcome.Invalid invalid)
        {
            return new PredictionOperationOutcome.InvalidTemplate(invalid.Message);
        }
        var valid = ((PredictionTemplateValidationOutcome.Valid)draft.Validate()).Draft;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await HostIsEnabledAsync(db, hostId, cancellationToken))
        {
            return Disabled();
        }

        var template = new TwitchPredictionTemplate
        {
            HostId = hostId,
            Title = valid.Title,
            PredictionWindowSeconds = valid.PredictionWindowSeconds,
            CreatedAtUtc = DateTime.UtcNow,
            Outcomes = valid
                .Outcomes.Select(
                    (title, position) =>
                        new TwitchPredictionTemplateOutcome { Title = title, Position = position }
                )
                .ToArray(),
        };
        _ = db.TwitchPredictionTemplates.Add(template);
        _ = await db.SaveChangesAsync(cancellationToken);
        await ChangedAsync(cancellationToken);
        return new PredictionOperationOutcome.TemplateSaved(View(template));
    }

    public async Task<PredictionOperationOutcome> DeleteTemplateAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return Disabled();
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var template = await db.TwitchPredictionTemplates.SingleOrDefaultAsync(
            x => x.Id == templateId && x.HostId == hostId,
            cancellationToken
        );
        if (template is null)
        {
            return new PredictionOperationOutcome.TemplateNotFound();
        }
        if (!await HostIsEnabledAsync(db, hostId, cancellationToken))
        {
            return Disabled();
        }

        _ = db.TwitchPredictionTemplates.Remove(template);
        _ = await db.SaveChangesAsync(cancellationToken);
        await ChangedAsync(cancellationToken);
        return new PredictionOperationOutcome.TemplateDeleted();
    }

    public async Task<PredictionOperationOutcome> StartAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return Disabled();
        }
        var token = await ReadyTokenAsync(hostId, cancellationToken);
        if (token is null)
        {
            return new PredictionOperationOutcome.NotReady(_notReadyMessage);
        }
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return Disabled();
        }
        if (
            await EligibilityAsync(token, cancellationToken)
            is { } eligibility
                and not HelixPredictionEligibilityOutcome.Eligible
        )
        {
            return EligibilityOutcome(eligibility);
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
        var template = await db
            .TwitchPredictionTemplates.Include(x => x.Outcomes)
            .SingleOrDefaultAsync(x => x.Id == templateId && x.HostId == hostId, cancellationToken);
        if (host?.TwitchUserId is not { Length: > 0 } || template is null)
        {
            return new PredictionOperationOutcome.TemplateNotFound();
        }
        if ((host.EnabledFeatures & HostFeatureFlags.Predictions) != HostFeatureFlags.Predictions)
        {
            return Disabled();
        }
        if (
            await db.TwitchPredictions.AnyAsync(
                x =>
                    x.HostId == hostId
                    && (
                        x.Status == TwitchPredictionStatus.Active
                        || x.Status == TwitchPredictionStatus.Locked
                    ),
                cancellationToken
            )
        )
        {
            return new PredictionOperationOutcome.ActivePredictionExists();
        }
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return Disabled();
        }

        var provider = await helix.CreatePredictionAsync(
            new(settings.Identity.ClientId, token),
            host.TwitchUserId,
            new(
                template.Title,
                template.Outcomes.OrderBy(x => x.Position).Select(x => x.Title).ToArray(),
                template.PredictionWindowSeconds
            ),
            cancellationToken
        );
        if (provider is HelixPredictionCreateOutcome.ActivePredictionExists)
        {
            await ReconcileAsync(hostId, cancellationToken);
            return new PredictionOperationOutcome.ActivePredictionExists();
        }
        if (provider is HelixPredictionCreateOutcome.Unauthorized)
        {
            return new PredictionOperationOutcome.NotReady(_notReadyMessage);
        }
        if (provider is HelixPredictionCreateOutcome.Ineligible)
        {
            return new PredictionOperationOutcome.Ineligible(_ineligibleMessage);
        }
        if (provider is HelixPredictionCreateOutcome.InvalidRequest)
        {
            return new PredictionOperationOutcome.ProviderRejected(
                "Twitch rejected this prediction request."
            );
        }
        if (provider is not HelixPredictionCreateOutcome.Created created)
        {
            return new PredictionOperationOutcome.Unavailable(
                "Twitch is temporarily unavailable; the prediction was not started locally."
            );
        }
        var prediction = Upsert(db, hostId, created.Prediction, false).Prediction;
        _ = await db.SaveChangesAsync(cancellationToken);
        await ChangedAsync(cancellationToken);
        return new PredictionOperationOutcome.Started(View(prediction));
    }

    public Task<PredictionOperationOutcome> LockAsync(
        int hostId,
        bool confirmed,
        CancellationToken cancellationToken
    ) => EndAsync(hostId, HelixPredictionEndStatus.Locked, null, confirmed, cancellationToken);

    public Task<PredictionOperationOutcome> CancelAsync(
        int hostId,
        bool confirmed,
        CancellationToken cancellationToken
    ) => EndAsync(hostId, HelixPredictionEndStatus.Canceled, null, confirmed, cancellationToken);

    public Task<PredictionOperationOutcome> ResolveAsync(
        int hostId,
        string winningOutcomeId,
        bool confirmed,
        CancellationToken cancellationToken
    ) =>
        EndAsync(
            hostId,
            HelixPredictionEndStatus.Resolved,
            winningOutcomeId,
            confirmed,
            cancellationToken
        );

    private async Task<PredictionOperationOutcome> EndAsync(
        int hostId,
        HelixPredictionEndStatus status,
        string? outcomeId,
        bool confirmed,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return Disabled();
        }

        var token = await ReadyTokenAsync(hostId, cancellationToken);
        if (token is null)
        {
            return new PredictionOperationOutcome.NotReady(_notReadyMessage);
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
        var active = await db.TwitchPredictions.SingleOrDefaultAsync(
            x =>
                x.HostId == hostId
                && (
                    x.Status == TwitchPredictionStatus.Active
                    || x.Status == TwitchPredictionStatus.Locked
                ),
            cancellationToken
        );
        if (host?.TwitchUserId is not { Length: > 0 } || active is null)
        {
            return new PredictionOperationOutcome.ProviderRejected(
                "There is no active prediction."
            );
        }
        if ((host.EnabledFeatures & HostFeatureFlags.Predictions) != HostFeatureFlags.Predictions)
        {
            return Disabled();
        }
        if (!confirmed)
        {
            return new PredictionOperationOutcome.ConfirmationRequired();
        }
        var outcomes =
            JsonSerializer.Deserialize<PredictionOutcomeView[]>(active.OutcomesJson) ?? [];
        if (status is HelixPredictionEndStatus.Resolved && !outcomes.Any(x => x.Id == outcomeId))
        {
            return new PredictionOperationOutcome.InvalidOutcome();
        }
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return Disabled();
        }

        var provider = await helix.EndPredictionAsync(
            new(settings.Identity.ClientId, token),
            host.TwitchUserId,
            active.ProviderPredictionId,
            status,
            outcomeId,
            cancellationToken
        );
        if (provider is HelixPredictionEndOutcome.Unauthorized)
        {
            return new PredictionOperationOutcome.NotReady(_notReadyMessage);
        }
        if (provider is HelixPredictionEndOutcome.Ineligible)
        {
            return new PredictionOperationOutcome.Ineligible(_ineligibleMessage);
        }
        if (provider is HelixPredictionEndOutcome.InvalidRequest)
        {
            return new PredictionOperationOutcome.ProviderRejected(
                "Twitch did not permit that prediction transition."
            );
        }
        if (provider is not HelixPredictionEndOutcome.Updated updated)
        {
            return new PredictionOperationOutcome.Unavailable(
                "Twitch is temporarily unavailable; the prediction was not changed locally."
            );
        }
        var prediction = Upsert(
            db,
            hostId,
            updated.Prediction,
            active.IsExternallyStarted
        ).Prediction;
        _ = await db.SaveChangesAsync(cancellationToken);
        if (
            Terminal(prediction.Status)
            && await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            await TrimAsync(db, hostId, cancellationToken);
            _ = await db.SaveChangesAsync(cancellationToken);
        }
        await ChangedAsync(cancellationToken);
        return new PredictionOperationOutcome.Updated(View(prediction));
    }

    public async Task ReconcileChannelAsync(string channel, CancellationToken cancellationToken)
    {
        var login = Login.Normalize(channel);
        if (login.Length == 0)
        {
            return;
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hostId = await db
            .Hosts.Where(x => x.Login == login)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (hostId is { } id)
        {
            await ReconcileAsync(id, cancellationToken);
        }
    }

    public async Task ReconcileAsync(int hostId, CancellationToken cancellationToken)
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return;
        }

        var token = await ReadyTokenAsync(hostId, cancellationToken);
        if (token is null)
        {
            return;
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
        if (
            host?.TwitchUserId is not { Length: > 0 } id
            || (host.EnabledFeatures & HostFeatureFlags.Predictions) != HostFeatureFlags.Predictions
        )
        {
            return;
        }
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return;
        }

        var provider = await helix.GetLatestPredictionAsync(
            new(settings.Identity.ClientId, token),
            id,
            cancellationToken
        );
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return;
        }

        var changed = false;
        if (provider is HelixPredictionLookupOutcome.Found found)
        {
            foreach (var prediction in found.Predictions)
            {
                changed |= Upsert(db, hostId, prediction, true).Changed;
            }
            if (
                !found.Predictions.Any(x =>
                    x.Status is HelixPredictionStatus.Active or HelixPredictionStatus.Locked
                )
            )
            {
                changed |= ArchiveMissingActive(db, hostId);
            }
        }
        else if (provider is HelixPredictionLookupOutcome.NoPrediction)
        {
            changed = ArchiveMissingActive(db, hostId);
        }
        if (!changed || !await HostIsEnabledAsync(db, hostId, cancellationToken))
        {
            return;
        }
        await SaveAndTrimAsync(db, hostId, cancellationToken);
        await ChangedAsync(cancellationToken);
    }

    public async Task PredictionReceivedAsync(
        EventSubPredictionEvent prediction,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                prediction.BroadcasterUserLogin,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(
            x =>
                x.TwitchUserId == prediction.BroadcasterUserId
                || x.Login == Login.Normalize(prediction.BroadcasterUserLogin),
            cancellationToken
        );
        if (
            host is null
            || (host.EnabledFeatures & HostFeatureFlags.Predictions) != HostFeatureFlags.Predictions
            || prediction.ToHelix().Status is HelixPredictionStatus.Unknown
        )
        {
            return;
        }
        if (prediction.ToHelix().Status is HelixPredictionStatus.Active)
        {
            QueueProgress(host.Id, prediction.ToHelix());
            return;
        }
        _ = _pendingProgress.TryRemove(host.Id, out _);
        var upsert = Upsert(db, host.Id, prediction.ToHelix(), true);
        if (!upsert.Changed)
        {
            return;
        }
        if (!await HostIsEnabledAsync(db, host.Id, cancellationToken))
        {
            return;
        }

        if (Terminal(upsert.Prediction.Status))
        {
            await SaveAndTrimAsync(db, host.Id, cancellationToken);
        }
        else
        {
            _ = await db.SaveChangesAsync(cancellationToken);
        }
        await ChangedAsync(cancellationToken);
    }

    private void QueueProgress(int hostId, HelixPrediction prediction)
    {
        while (true)
        {
            if (_pendingProgress.TryGetValue(hostId, out var current))
            {
                var merged = current with
                {
                    Prediction = MergeProgress(current.Prediction, prediction),
                };
                if (_pendingProgress.TryUpdate(hostId, merged, current))
                {
                    return;
                }
                continue;
            }
            if (_pendingProgress.TryAdd(hostId, new PendingProgress(prediction)))
            {
                _ = FlushProgressAsync(hostId);
                return;
            }
        }
    }

    private async Task FlushProgressAsync(int hostId)
    {
        PredictionProgressFlushDecision decision;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ProgressTimeProvider);
            if (
                !await nativeTwitch.IsEnabledAsync(
                    hostId,
                    HostFeatureFlags.Predictions,
                    CancellationToken.None
                )
            )
            {
                _ = _pendingProgress.TryRemove(hostId, out _);
                decision = PredictionProgressFlushDecision.SkippedNativeTwitchDisabled;
            }
            else if (!_pendingProgress.TryRemove(hostId, out var pending))
            {
                decision = PredictionProgressFlushDecision.SkippedNoPendingProgress;
            }
            else
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var upsert = Upsert(db, hostId, pending.Prediction, true);
                if (!upsert.Changed)
                {
                    decision = PredictionProgressFlushDecision.SkippedNoChange;
                }
                else if (!await HostIsEnabledAsync(db, hostId, CancellationToken.None))
                {
                    decision = PredictionProgressFlushDecision.SkippedNativeTwitchDisabled;
                }
                else
                {
                    _ = await db.SaveChangesAsync();
                    await ChangedAsync(CancellationToken.None);
                    decision = PredictionProgressFlushDecision.Persisted;
                }
            }
        }
        catch (Exception exception)
        {
            decision = PredictionProgressFlushDecision.Failed;
            logger.LogWarning(
                exception,
                "Prediction progress debounce failed for host {HostId}.",
                hostId
            );
        }
        CompleteProgressFlush(hostId, decision);
    }

    private void CompleteProgressFlush(int hostId, PredictionProgressFlushDecision decision)
    {
        if (_progressFlushObservers.TryRemove(hostId, out var observer))
        {
            _ = observer.TrySetResult(decision);
        }
    }

    private async Task<PredictionAuthorizationReadiness> ReadinessAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        if (
            !await nativeTwitch.IsEnabledAsync(
                hostId,
                HostFeatureFlags.Predictions,
                cancellationToken
            )
        )
        {
            return new PredictionAuthorizationReadiness.Disabled();
        }

        var token = await ReadyTokenAsync(hostId, cancellationToken);
        if (token is null)
        {
            return new PredictionAuthorizationReadiness.NeedsBroadcasterAuthorization(
                _notReadyMessage
            );
        }

        var enabled = await nativeTwitch.IsEnabledAsync(
            hostId,
            HostFeatureFlags.Predictions,
            cancellationToken
        );
        return enabled switch
        {
            false => new PredictionAuthorizationReadiness.Disabled(),
            true => await EligibilityAsync(token, cancellationToken) switch
            {
                HelixPredictionEligibilityOutcome.Eligible =>
                    new PredictionAuthorizationReadiness.Ready(),
                HelixPredictionEligibilityOutcome.Ineligible =>
                    new PredictionAuthorizationReadiness.Ineligible(_ineligibleMessage),
                HelixPredictionEligibilityOutcome.Unauthorized =>
                    new PredictionAuthorizationReadiness.NeedsBroadcasterAuthorization(
                        _notReadyMessage
                    ),
                _ => new PredictionAuthorizationReadiness.Unavailable(
                    "Twitch eligibility could not be checked right now."
                ),
            },
        };
    }

    private async Task<HelixPredictionEligibilityOutcome> EligibilityAsync(
        string token,
        CancellationToken cancellationToken
    ) =>
        await helix.GetPredictionEligibilityAsync(
            new(settings.Identity.ClientId, token),
            cancellationToken
        );

    private static PredictionOperationOutcome Disabled() =>
        new PredictionOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);

    private static Task<bool> HostIsEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        db.Hosts.AnyAsync(
            host =>
                host.Id == hostId
                && (host.EnabledFeatures & HostFeatureFlags.Predictions)
                    == HostFeatureFlags.Predictions,
            cancellationToken
        );

    private static PredictionOperationOutcome EligibilityOutcome(
        HelixPredictionEligibilityOutcome outcome
    ) =>
        outcome switch
        {
            HelixPredictionEligibilityOutcome.Ineligible =>
                new PredictionOperationOutcome.Ineligible(_ineligibleMessage),
            HelixPredictionEligibilityOutcome.Unauthorized =>
                new PredictionOperationOutcome.NotReady(_notReadyMessage),
            _ => new PredictionOperationOutcome.Unavailable(
                "Twitch eligibility could not be checked right now."
            ),
        };

    private async Task<string?> ReadyTokenAsync(int hostId, CancellationToken cancellationToken) =>
        await broadcasters.GetTokenStatusAsync(
            hostId,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            cancellationToken
        )
            is TokenStatus.Ready ready
            ? ready.AccessToken
            : await MissingTokenAsync(hostId, cancellationToken);

    private async Task<string?> MissingTokenAsync(int hostId, CancellationToken cancellationToken)
    {
        await AlertAsync(hostId, cancellationToken);
        return null;
    }

    private async Task AlertAsync(int hostId, CancellationToken cancellationToken) =>
        await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "twitch-broadcaster-authorization",
                "reauthorize-v1",
                "Reconnect Twitch integration",
                "Reconnect the selected channel's Twitch integration and approve all requested permissions.",
                "/twitch-operations"
            )
            .ExecuteAsync(cancellationToken);

    private async Task ChangedAsync(CancellationToken cancellationToken) =>
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, cancellationToken);

    private static bool ArchiveMissingActive(BlokeBotDbContext db, int hostId)
    {
        var active = db.TwitchPredictions.SingleOrDefault(x =>
            x.HostId == hostId
            && (
                x.Status == TwitchPredictionStatus.Active
                || x.Status == TwitchPredictionStatus.Locked
            )
        );
        if (active is null)
        {
            return false;
        }
        active.Status = TwitchPredictionStatus.Archived;
        active.EndedAtUtc = active.UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    private static PredictionUpsertOutcome Upsert(
        BlokeBotDbContext db,
        int hostId,
        HelixPrediction prediction,
        bool external
    )
    {
        var record =
            db.TwitchPredictions.Local.SingleOrDefault(x =>
                x.HostId == hostId && x.ProviderPredictionId == prediction.Id
            )
            ?? db.TwitchPredictions.SingleOrDefault(x =>
                x.HostId == hostId && x.ProviderPredictionId == prediction.Id
            );
        if (prediction.Status is HelixPredictionStatus.Unknown)
        {
            return record is null
                ? new PredictionUpsertOutcome(new TwitchPrediction(), false)
                : new PredictionUpsertOutcome(record, false);
        }
        var status = ToPersisted(prediction.Status);
        if (
            record is not null
            && (Terminal(record.Status) || StateRank(status) < StateRank(record.Status))
        )
        {
            return new(record, false);
        }
        if (record is null)
        {
            record = new TwitchPrediction
            {
                HostId = hostId,
                ProviderPredictionId = prediction.Id,
                IsExternallyStarted = external,
            };
            _ = db.TwitchPredictions.Add(record);
        }
        var outcomes = ToProjection(prediction.Outcomes);
        var outcomesJson = JsonSerializer.Serialize(outcomes);
        var previous =
            JsonSerializer.Deserialize<PredictionOutcomeView[]>(record.OutcomesJson) ?? [];
        if (record.Status == status && record.OutcomesJson == outcomesJson)
        {
            return new(record, false);
        }
        if (
            status is TwitchPredictionStatus.Active
            && HasParticipationRegression(previous, outcomes)
        )
        {
            return new(record, false);
        }
        record.Title = prediction.Title;
        record.OutcomesJson = outcomesJson;
        record.Status = status;
        record.CreatedAtUtc = prediction.CreatedAt.UtcDateTime;
        record.LocksAtUtc = prediction.LocksAt?.UtcDateTime;
        record.EndedAtUtc = Terminal(status)
            ? prediction.EndedAt?.UtcDateTime ?? DateTime.UtcNow
            : null;
        record.UpdatedAtUtc = DateTime.UtcNow;
        return new(record, true);
    }

    private static HelixPrediction MergeProgress(HelixPrediction current, HelixPrediction incoming)
    {
        if (
            current.Status is not HelixPredictionStatus.Active
            || incoming.Status is not HelixPredictionStatus.Active
        )
        {
            return incoming;
        }
        var outcomes = current
            .Outcomes.Select(old =>
                incoming.Outcomes.FirstOrDefault(next => next.Id == old.Id) is { } next
                && next.Users >= old.Users
                && next.ChannelPoints >= old.ChannelPoints
                    ? next
                    : old
            )
            .ToArray();
        return incoming with { Outcomes = outcomes };
    }

    private static PredictionOutcomeView[] ToProjection(
        IReadOnlyList<HelixPredictionOutcome> outcomes
    ) =>
        outcomes
            .Select(static x => new PredictionOutcomeView(
                x.Id,
                x.Title,
                x.Color,
                x.Users,
                x.ChannelPoints,
                x.TopPredictors.Select(static p => new PredictionTopPredictorView(
                        p.UserLogin,
                        p.UserName,
                        p.ChannelPointsUsed,
                        p.ChannelPointsWon
                    ))
                    .ToArray()
            ))
            .ToArray();

    private static bool HasParticipationRegression(
        IReadOnlyList<PredictionOutcomeView> previous,
        IReadOnlyList<PredictionOutcomeView> current
    ) =>
        previous.Any(old =>
            current.FirstOrDefault(next => next.Id == old.Id) is not { } next
            || next.Users < old.Users
            || next.ChannelPoints < old.ChannelPoints
        );

    private static TwitchPredictionStatus ToPersisted(HelixPredictionStatus value) =>
        value switch
        {
            HelixPredictionStatus.Active => TwitchPredictionStatus.Active,
            HelixPredictionStatus.Locked => TwitchPredictionStatus.Locked,
            HelixPredictionStatus.Resolved => TwitchPredictionStatus.Resolved,
            HelixPredictionStatus.Canceled => TwitchPredictionStatus.Canceled,
            _ => TwitchPredictionStatus.Archived,
        };

    private static int StateRank(TwitchPredictionStatus value) =>
        value switch
        {
            TwitchPredictionStatus.Active => 1,
            TwitchPredictionStatus.Locked => 2,
            TwitchPredictionStatus.Resolved or TwitchPredictionStatus.Canceled => 3,
            _ => 4,
        };

    private static bool Terminal(TwitchPredictionStatus value) =>
        value is not TwitchPredictionStatus.Active and not TwitchPredictionStatus.Locked;

    private static async Task SaveAndTrimAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        _ = await db.SaveChangesAsync(cancellationToken);
        await TrimAsync(db, hostId, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task TrimAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var excess = await db
            .TwitchPredictions.Where(x =>
                x.HostId == hostId
                && x.Status != TwitchPredictionStatus.Active
                && x.Status != TwitchPredictionStatus.Locked
            )
            .OrderByDescending(x => x.EndedAtUtc)
            .Skip(_resultsToKeep)
            .ToArrayAsync(cancellationToken);
        db.TwitchPredictions.RemoveRange(excess);
    }

    private static PredictionTemplateView View(TwitchPredictionTemplate template) =>
        new(
            template.Id,
            template.Title,
            template.Outcomes.OrderBy(static x => x.Position).Select(static x => x.Title).ToArray(),
            template.PredictionWindowSeconds
        );

    private static PredictionView View(TwitchPrediction value) =>
        new(
            value.ProviderPredictionId,
            value.Title,
            JsonSerializer.Deserialize<PredictionOutcomeView[]>(value.OutcomesJson) ?? [],
            value.Status.ToString(),
            value.IsExternallyStarted,
            value.CreatedAtUtc,
            value.LocksAtUtc,
            value.EndedAtUtc
        );

    private sealed record PredictionUpsertOutcome(TwitchPrediction Prediction, bool Changed);

    private sealed record PendingProgress(HelixPrediction Prediction);
}

internal enum PredictionProgressFlushDecision
{
    SkippedNativeTwitchDisabled,
    SkippedNoPendingProgress,
    SkippedNoChange,
    Persisted,
    Failed,
}
