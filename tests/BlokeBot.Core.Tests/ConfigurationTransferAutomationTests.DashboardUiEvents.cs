using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DashboardUiEvents_ActualImportPublishesOnlyAfterCommit_AndRollbackPreservesTimer(
        bool failAudit
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(
            failAudit ? [new FailImportAuditSaveInterceptor()] : []
        );
        var (hostId, _, _, documentId) = await SeedAsync(database);
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = await seed.Hosts.SingleAsync(h => h.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.All;
            var now = DateTime.UtcNow;
            _ = seed.AutomationCountdowns.Add(
                new()
                {
                    HostId = hostId,
                    Name = "import-timer",
                    OccurrenceId = Guid.NewGuid(),
                    ProcessId = Guid.NewGuid(),
                    IsRunning = true,
                    StartedAtUtc = now,
                    DeadlineUtc = now.AddMinutes(5),
                    ObservedAtUtc = now,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var changes = TestAutomationUiChanges.Create();
        var observed = new List<bool>();
        using var subscription = changes.Subscribe(
            new AutomationUiChange.CountdownHost(new(hostId)),
            ObserverIdentity.Named("actual-import-commit"),
            async (_, _) =>
            {
                await using var read = await database.CreateDbContextAsync();
                observed.Add((await read.AutomationCountdowns.SingleAsync()).WasCancelled);
            }
        );
        var observers = new ConfigurationImportObserverDispatcher(
            [new CountdownConfigurationImportObserver(changes)],
            NullLogger<ConfigurationImportObserverDispatcher>.Instance
        );
        var imported = Document(documentId);
        imported = imported with
        {
            Sections = imported.Sections with
            {
                ChannelToolEnablement = ChannelToolEnablementMapper.FromFlags(
                    HostFeatureFlags.All & ~HostFeatureFlags.Automations
                ),
            },
        };
        var selection = new ConfigurationImportSelection(
            hostId,
            [new(ConfigurationSectionId.ChannelToolEnablement, ImportConflictStrategy.Merge, [])],
            new HashSet<HostFeatureFlags> { HostFeatureFlags.Automations }
        );
        var result = await Coordinator(
                database,
                NullLogger<ConfigurationTransferCoordinator>.Instance,
                observers,
                uiChanges: changes
            )
            .ApplyAsync(
                Session(hostId),
                imported,
                await ReviewImportEffectsAsync(database, imported, selection),
                new("destination-id", "destination"),
                CancellationToken.None
            );
        await using var verify = await database.CreateDbContextAsync();
        var timer = await verify.AutomationCountdowns.SingleAsync();
        if (failAudit)
        {
            _ = result.ShouldBeOfType<ConfigurationImportApplyOutcome.Failed>();
            observed.ShouldBeEmpty();
            timer.IsRunning.ShouldBeTrue();
            timer.WasCancelled.ShouldBeFalse();
        }
        else
        {
            _ = result.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
            observed.ShouldBe([true]);
            timer.IsRunning.ShouldBeFalse();
            timer.WasCancelled.ShouldBeTrue();
        }
    }
}
