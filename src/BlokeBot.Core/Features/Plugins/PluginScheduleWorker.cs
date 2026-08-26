using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

internal sealed class PluginScheduleWorker(
    IPluginScheduleStore store,
    IPluginDispatchSnapshotProvider dispatch,
    IPluginFeatureSnapshotProvider features,
    IPluginDispatchInvoker invoker,
    TimeProvider timeProvider,
    ILogger<PluginScheduleWorker> logger,
    IPluginAutomationSourceAdmission? automationSources = null
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var featureVersion = features.CurrentVersion;
        if (featureVersion.Value == 0)
        {
            _ = await features.WaitForChangeAsync(featureVersion, stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            var schedules = await store.LoadAsync(stoppingToken);
            var now = timeProvider.GetUtcNow();
            foreach (var schedule in schedules.Where(entry => entry.DueAtUtc <= now))
            {
                await ProcessAsync(schedule, now, stoppingToken);
            }
            var next = schedules
                .Where(entry => entry.DueAtUtc > now)
                .MinBy(entry => entry.DueAtUtc);
            var delay = next is null
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromMilliseconds(
                    Math.Clamp((next.DueAtUtc - now).TotalMilliseconds, 10, 1_000)
                );
            await Task.Delay(delay, timeProvider, stoppingToken);
        }
    }

    private async Task ProcessAsync(
        PluginScheduleEntry schedule,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var endpoint = dispatch.Current.Schedules.SingleOrDefault(candidate =>
            candidate.State.Key == schedule.Feature
            && candidate.State.Fence == schedule.Fence.Lifecycle
            && candidate.State.Generation == schedule.Fence.FeatureGeneration
            && candidate.Descriptor.Id == schedule.HandlerId
        );
        if (endpoint is null)
        {
            if (
                !features.Current.States.TryGetValue(schedule.Feature, out var state)
                || !state.Enabled
                || state.Fence != schedule.Fence.Lifecycle
                || state.Generation != schedule.Fence.FeatureGeneration
            )
            {
                await store.RemoveAsync(schedule.Id, cancellationToken);
            }
            return;
        }
        var context = new PluginInvocationContext.Channel(
            endpoint.Declaration.Installation,
            schedule.Feature.HostId,
            Schedule: new(schedule.HandlerId, schedule.Id, schedule.DueAtUtc)
        );
        DateTimeOffset? nextDueAtUtc = null;
        if (schedule.IntervalSeconds is { } interval)
        {
            nextDueAtUtc = schedule.DueAtUtc;
            do
            {
                nextDueAtUtc = nextDueAtUtc.Value.AddSeconds(interval);
            } while (nextDueAtUtc <= now);
        }
        if (!await store.TryConsumeOccurrenceAsync(schedule, nextDueAtUtc, cancellationToken))
        {
            return;
        }
        var outcome = await invoker.InvokeScheduleAsync(
            endpoint,
            context,
            schedule.Input,
            cancellationToken
        );
        if (
            outcome is PluginDispatchInvocationOutcome.Returned returned
            && !returned.AutomationSources.IsDefaultOrEmpty
            && automationSources is not null
        )
        {
            await automationSources.AdmitAsync(
                endpoint,
                context,
                returned.AutomationSources,
                cancellationToken
            );
        }
        if (outcome is PluginDispatchInvocationOutcome.Failed failed)
        {
            logger.LogWarning(
                "Plugin schedule {ScheduleId} failed with {FailureCode}.",
                schedule.Id,
                failed.Failure.Code
            );
        }
    }
}
