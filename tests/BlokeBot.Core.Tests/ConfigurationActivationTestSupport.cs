using System.Text.Json;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationActivationTests
{
    private static HostFeatureActivationAuthority Authority(
        IHostFeatureActivationObserver observer,
        EventBus<AppEventKind> events,
        DurableAlertService alerts
    ) => Authority([observer], events, alerts);

    private static HostFeatureActivationAuthority Authority(
        IReadOnlyList<IHostFeatureActivationObserver> observers,
        EventBus<AppEventKind> events,
        DurableAlertService alerts
    ) =>
        new(
            observers,
            new HostedChannelChangeNotifier(events),
            alerts,
            NullLogger<HostFeatureActivationAuthority>.Instance
        );

    private static ConfigurationActivationWorker Worker(
        SqliteBlokeBotDbFactory database,
        ConfigurationActivationQueue queue,
        HostFeatureActivationAuthority authority
    ) =>
        new(
            database,
            queue,
            authority,
            TimeProvider.System,
            NullLogger<ConfigurationActivationWorker>.Instance
        );

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory database,
        string login,
        HostFeatureFlags enabled
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            EnabledFeatures = enabled,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedActivationAsync(
        SqliteBlokeBotDbFactory database,
        Guid activationId,
        int hostId,
        HostFeatureFlags enabled,
        HostFeatureFlags disabled,
        DateTime? updatedAt = null
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var now = updatedAt ?? DateTime.UtcNow;
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = (host.EnabledFeatures | enabled) & ~disabled;
        _ = db.ConfigurationActivations.Add(
            new()
            {
                Id = activationId,
                HostId = hostId,
                EnabledChanges = enabled,
                DisabledChanges = disabled,
                Status = ConfigurationActivationStatus.Pending,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static async Task WaitForStatusAsync(
        SqliteBlokeBotDbFactory database,
        Guid activationId,
        ConfigurationActivationStatus expected,
        int minimumAttemptCount = 0
    )
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var db = await database.CreateDbContextAsync();
            var row = await db.ConfigurationActivations.SingleAsync(x => x.Id == activationId);
            if (row.Status == expected && row.AttemptCount >= minimumAttemptCount)
            {
                return;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException($"Activation {activationId} did not become {expected}.");
    }

    private static IReadOnlyList<ConfigurationActivationIssue> PersistedIssues(
        ConfigurationActivation activation
    ) =>
        JsonSerializer.Deserialize<ConfigurationActivationIssue[]>(
            activation.IssuesJson.ShouldNotBeNull()
        ) ?? [];

    private sealed class RecordingObserver(TimeSpan? delay = null) : IHostFeatureActivationObserver
    {
        public List<HostFeatureActivationChange> Changes { get; } = [];

        public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        )
        {
            Changes.Add(change);
            if (delay is { } value)
            {
                await Task.Delay(value, cancellationToken);
            }
            return new HostFeatureAutomaticWorkResult.Complete();
        }
    }
}
