using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Plugins.Features.Tests;

public sealed class PluginAutomationPersistenceTests
{
    [Test]
    public async Task Enable_NameCollisionRejectsWithoutOverwritingOrEnablingFeature()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        var existingId = Guid.NewGuid();
        await using (var seed = context.Database.CreateDbContext())
        {
            _ = seed.AutomationFlows.Add(
                new()
                {
                    Id = existingId,
                    HostId = 1,
                    Name = "Publish approved links",
                    SchemaVersion = 1,
                    IsEnabled = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var key = PluginFeatureTestContext.Key("publishing");

        var outcome = await context.Store.EnableAsync(
            new(
                null,
                State(key, PluginFeatureTestContext.Fence(), 1),
                PluginConfigurationRevision.Initial,
                PluginConfigurationRevision.Initial,
                Plan(Guid.NewGuid(), "template-hash-a")
            ),
            CancellationToken.None
        );

        outcome
            .ShouldBeOfType<PluginFeatureEnableStoreOutcome.Conflict>()
            .Code.ShouldBe(PluginFeatureEnableConflictCode.AutomationName);
        await using var verify = context.Database.CreateDbContext();
        var retained = await verify.AutomationFlows.SingleAsync();
        retained.Id.ShouldBe(existingId);
        retained.IsEnabled.ShouldBeTrue();
        (await verify.PluginFeatureStates.CountAsync()).ShouldBe(0);
        var rejected = await verify.PluginAutomationInstantiations.SingleAsync();
        rejected.Status.ShouldBe(PluginAutomationInstantiationStatus.Rejected);
        rejected.Diagnostic.ShouldBe("flow-name-conflict");
    }

    [Test]
    public async Task EnableLedger_IsIdempotentRecreatesDeletedFlowAndPreservesFlowOnPurge()
    {
        await using var context = await PluginFeatureTestContext.CreateAsync();
        var key = PluginFeatureTestContext.Key("publishing");
        var fence = PluginFeatureTestContext.Fence();
        var operationId = Guid.NewGuid();
        var plan = Plan(operationId, "template-hash-a");
        var firstState = State(key, fence, 1);

        _ = (
            await context.Store.EnableAsync(
                new(
                    null,
                    firstState,
                    PluginConfigurationRevision.Initial,
                    PluginConfigurationRevision.Initial,
                    plan
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginFeatureEnableStoreOutcome.Enabled>();
        var secondState = State(key, fence, 2);
        _ = (
            await context.Store.EnableAsync(
                new(
                    firstState,
                    secondState,
                    PluginConfigurationRevision.Initial,
                    PluginConfigurationRevision.Initial,
                    plan
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginFeatureEnableStoreOutcome.Enabled>();

        Guid originalFlowId;
        await using (var verify = context.Database.CreateDbContext())
        {
            originalFlowId = (await verify.AutomationFlows.SingleAsync()).Id;
            var ledger = await verify.PluginAutomationInstantiations.SingleAsync();
            ledger.EnableOperationId.ShouldBe(operationId);
            ledger.Status.ShouldBe(PluginAutomationInstantiationStatus.Completed);
            ledger.FlowId.ShouldBe(originalFlowId);

            _ = verify.AutomationFlows.Remove(await verify.AutomationFlows.SingleAsync());
            _ = await verify.SaveChangesAsync();
        }

        var thirdState = State(key, fence, 3);
        _ = (
            await context.Store.EnableAsync(
                new(
                    secondState,
                    thirdState,
                    PluginConfigurationRevision.Initial,
                    PluginConfigurationRevision.Initial,
                    Plan(Guid.NewGuid(), "template-hash-a")
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginFeatureEnableStoreOutcome.Enabled>();

        Guid recreatedFlowId;
        await using (var verify = context.Database.CreateDbContext())
        {
            recreatedFlowId = (await verify.AutomationFlows.SingleAsync()).Id;
            recreatedFlowId.ShouldNotBe(originalFlowId);
            (await verify.PluginAutomationInstantiations.CountAsync()).ShouldBe(2);
        }

        var incompatible = await context.Store.EnableAsync(
            new(
                thirdState,
                State(key, fence, 4),
                PluginConfigurationRevision.Initial,
                PluginConfigurationRevision.Initial,
                Plan(Guid.NewGuid(), "template-hash-b")
            ),
            CancellationToken.None
        );
        incompatible
            .ShouldBeOfType<PluginFeatureEnableStoreOutcome.Conflict>()
            .Code.ShouldBe(PluginFeatureEnableConflictCode.AutomationProvenance);

        await using (var verify = context.Database.CreateDbContext())
        {
            var retained = await verify.AutomationFlows.SingleAsync();
            retained.Id.ShouldBe(recreatedFlowId);
            retained.IsEnabled.ShouldBeFalse();
            retained.UnavailableReason.ShouldNotBeNull().ShouldContain("changed");
            (await verify.PluginAutomationInstantiations.CountAsync()).ShouldBe(3);
            (
                await context.Store.LoadFeatureStateAsync(key, CancellationToken.None)
            )!.Revision.ShouldBe(thirdState.Revision);
            (
                await context.Store.HasFormat1IncompatibleStateAsync(
                    key.HostId,
                    CancellationToken.None
                )
            ).ShouldBeTrue();
        }

        await context.Store.PurgeAsync(key.PluginId, CancellationToken.None);

        await using var purged = context.Database.CreateDbContext();
        (await purged.PluginAutomationInstantiations.CountAsync()).ShouldBe(0);
        var hostFlow = await purged.AutomationFlows.SingleAsync();
        hostFlow.Id.ShouldBe(recreatedFlowId);
        hostFlow.IsEnabled.ShouldBeFalse();
    }

    private static PluginFeatureState State(
        PluginFeatureKey key,
        PluginLifecycleFence fence,
        ulong value
    )
    {
        PluginFeatureGeneration.TryCreate(value, out var generation).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(checked((long)value), out var revision).ShouldBeTrue();
        return new(key, fence, generation, new PluginFeatureReadiness.Ready(), revision);
    }

    private static PluginAutomationEnableStorePlan Plan(Guid operationId, string templateHash)
    {
        var sourceNode = Guid.NewGuid();
        return new(
            operationId,
            "community.link-queue",
            "1.2.0",
            "community-link-queue",
            1,
            "publishing",
            [
                new(
                    "Publish approved links",
                    new(
                        "community.link-queue",
                        "1.2.0",
                        "community-link-queue",
                        1,
                        "publishing",
                        "publish-links",
                        templateHash
                    ),
                    [
                        new(
                            sourceNode,
                            "plugin.community.link-queue.queued-link",
                            1,
                            "{}",
                            "{}",
                            "{}",
                            false,
                            48,
                            72
                        ),
                    ],
                    []
                ),
            ]
        );
    }
}
