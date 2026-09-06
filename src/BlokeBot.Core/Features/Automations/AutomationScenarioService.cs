using System.Collections.Immutable;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationScenarioService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationCatalogService catalog,
    AutomationFlowService flows
)
{
    public const int MaximumScenariosPerFlow = 32;
    public const int MaximumFixtureBytes = 65_536;
    public const int MaximumGraphNodes = 256;
    public const int MaximumGraphEdges = 1024;

    public async Task<AutomationScenarioRunOutcome> RunAsync(
        AutomationFlowDraft draft,
        AutomationScenarioFixture fixture,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await ValidateAsync(draft, fixture, cancellationToken);
        if (validation.Gate == AutomationCatalogAvailability.Disabled)
        {
            return new AutomationScenarioRunOutcome.FeatureDisabled();
        }
        if (validation.Gate == AutomationCatalogAvailability.HostNotFound)
        {
            return new AutomationScenarioRunOutcome.HostNotFound();
        }
        if (!validation.Errors.IsEmpty)
        {
            return new AutomationScenarioRunOutcome.Invalid(validation.Errors);
        }
        var frozen = AutomationScenarioGraph.FreezeGraph(ApplyConfigurations(draft, fixture));
        var context = fixture.Context;
        var now = fixture.ClockUtc.UtcDateTime;
        var outcomes = ImmutableArray.CreateBuilder<AutomationScenarioNodeOutcome>();
        outcomes.Add(
            new(
                fixture.SourceNodeId,
                AutomationNodeRunState.Succeeded,
                "source-received",
                [],
                new(now, TimeSpan.Zero)
            )
        );
        var checkpoints = new AutomationScenarioCheckpointStore();
        var entropy = new AutomationSeededIntegerEntropy(fixture.Seed);
        var pending = new Queue<PendingNode>();
        var scheduled = new HashSet<Guid> { fixture.SourceNodeId.Value };
        Schedule(fixture.SourceNodeId.Value, null, now);
        while (pending.TryDequeue(out var pendingNode))
        {
            cancellationToken.ThrowIfCancellationRequested();
            now = now > pendingNode.AvailableAtUtc ? now : pendingNode.AvailableAtUtc;
            var node = frozen.Nodes.Single(candidate => candidate.Id == pendingNode.Id);
            var check = catalog.ValidatePersistedDefinition(
                AutomationRuntimeSerialization.Definition(node)
            );
            if (check is not AutomationConfigurationCheck.Valid valid)
            {
                return new AutomationScenarioRunOutcome.Invalid([
                    new(
                        new(node.Id),
                        "definition-unavailable",
                        "Restore this node definition before testing."
                    ),
                ]);
            }
            var inputs = await catalog.Data.ResolveScenarioInputsAsync(
                draft.HostId,
                context,
                frozen,
                node,
                checkpoints,
                entropy,
                fixture.ConnectedInputs,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            var execution = inputs is AutomationInputResolution.Available available
                ? AutomationScenarioSimulation.Evaluate(
                    valid.Configuration,
                    available.FieldValues,
                    fixture.Effects.FirstOrDefault(effect => effect.NodeId.Value == node.Id)?.Result
                        ?? AutomationScenarioEffectResult.Succeeded,
                    now
                )
                : new AutomationNodeExecution.Failed(
                    ((AutomationInputResolution.Failed)inputs).Code
                );
            var diagnostics = inputs is AutomationInputResolution.Available resolved
                ? AutomationDataValueSerialization.Diagnostics(resolved.PortValues)
                : [];
            if (execution is AutomationNodeExecution.Failed failed)
            {
                outcomes.Add(
                    new(
                        new(node.Id),
                        node.ContinueOnFailure
                            ? AutomationNodeRunState.ContinuedAfterFailure
                            : AutomationNodeRunState.Failed,
                        failed.Code,
                        diagnostics,
                        new(now, TimeSpan.Zero)
                    )
                );
                if (!node.ContinueOnFailure)
                {
                    return new AutomationScenarioRunOutcome.Failed(outcomes.ToImmutable());
                }
                Schedule(node.Id, "complete", now);
            }
            else if (execution is AutomationNodeExecution.Succeeded succeeded)
            {
                outcomes.Add(
                    new(
                        new(node.Id),
                        AutomationNodeRunState.Succeeded,
                        succeeded.Code,
                        diagnostics,
                        new(now, TimeSpan.Zero)
                    )
                );
                Schedule(node.Id, succeeded.OutputPort, succeeded.NextAvailableAtUtc);
            }
        }
        return new AutomationScenarioRunOutcome.Completed(outcomes.ToImmutable());

        void Schedule(Guid nodeId, string? port, DateTime due)
        {
            foreach (
                var target in AutomationFlowTraversal.Schedule(
                    frozen.Edges,
                    nodeId,
                    port,
                    scheduled
                )
            )
            {
                pending.Enqueue(new(target, due));
            }
        }
    }

    private static AutomationFlowDraft ApplyConfigurations(
        AutomationFlowDraft draft,
        AutomationScenarioFixture fixture
    ) =>
        draft with
        {
            Nodes =
            [
                .. draft.Nodes.Select(node =>
                    fixture.Configurations.FirstOrDefault(value => value.NodeId == node.Id)
                        is { } replacement
                        ? node with
                        {
                            Definition = replacement.Definition,
                        }
                        : node
                ),
            ],
        };

    private sealed record PendingNode(Guid Id, DateTime AvailableAtUtc);
}
