using System.Collections.Immutable;
using BlokeBot.Core.Features.Overlays;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    private static void ValidateTriggerContexts(
        IReadOnlyDictionary<AutomationNodeId, AutomationFlowDraftNode> nodes,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        IReadOnlyCollection<AutomationFlowDraftNode> sources,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> adjacency,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        foreach (var node in nodes.Values)
        {
            if (
                !definitions.TryGetValue(node.Id, out var definition)
                || definition.TriggerContextRequirement is not { } requirement
            )
            {
                continue;
            }

            var hasCompatiblePath = sources
                .Where(source =>
                    requirement.CompatibleSources.Contains(new(source.Definition.TypeId))
                )
                .Any(source => Reachable([source.Id], adjacency).Contains(node.Id));
            if (!hasCompatiblePath)
            {
                errors.Add(
                    new(node.Id, "trigger-context-incompatible", requirement.ValidationMessage)
                );
            }
        }
    }

    private async Task ValidateNodeAsync(
        AutomationHostId hostId,
        AutomationFlowDraftNode node,
        ImmutableArray<AutomationGraphError>.Builder errors,
        AutomationGraphAdmission admission,
        CancellationToken cancellationToken
    )
    {
        var check = admission
            is AutomationGraphAdmission.Frozen
                or AutomationGraphAdmission.ConfigurationTransfer
            ? catalog.ValidatePersistedDefinition(node.Definition)
            : await catalog.ValidatePersistedForSaveAsync(
                hostId,
                node.Definition,
                cancellationToken
            );
        if (check is AutomationConfigurationCheck.Invalid invalid)
        {
            foreach (var error in invalid.Errors)
            {
                errors.Add(
                    new(
                        node.Id,
                        "configuration-invalid",
                        error.Message,
                        error.Target is AutomationValidationTarget.Field field ? field.Id : null,
                        error.Target is AutomationValidationTarget.Port port ? port.Id : null
                    )
                );
            }

            return;
        }

        if (check is not AutomationConfigurationCheck.Valid valid)
        {
            errors.Add(
                new(node.Id, "configuration-invalid", "Restore this node type, or delete the node.")
            );
            return;
        }

        if (!Enum.IsDefined(node.FailurePolicy))
        {
            errors.Add(new(node.Id, "failure-policy-invalid", "Choose Stop or Continue."));
        }

        if (node.ExpressionLanguageVersion != AutomationExpressionLanguage.CurrentVersion)
        {
            errors.Add(
                new(
                    node.Id,
                    "expression-version-unsupported",
                    "Replace this node. Its expression version is not supported."
                )
            );
        }

        if (
            admission == AutomationGraphAdmission.Saved
            && valid.Configuration is PlayOverlayCueActionConfiguration cue
        )
        {
            var references = await overlayCues.ResolveReferencesAsync(
                new(hostId.Value, cue.TargetId.Value, cue.CueId.Value),
                cancellationToken
            );
            if (references is not OverlayCueReferenceOutcome.Available)
            {
                errors.Add(
                    new(
                        node.Id,
                        "overlay-reference-unavailable",
                        "Choose an available Cue player and saved cue."
                    )
                );
            }
        }

        if (
            admission == AutomationGraphAdmission.Saved
            && valid.Configuration is RewardRedemptionSourceConfiguration { RewardId: { } rewardId }
        )
        {
            // The reward filter is a reference resolved against this channel's known rewards,
            // never free-text. Externally created rewards remain valid read-only triggers.
            await using var rewardDb = await dbFactory.CreateDbContextAsync(cancellationToken);
            var known = await rewardDb
                .TwitchCustomRewards.AsNoTracking()
                .AnyAsync(
                    reward => reward.HostId == hostId.Value && reward.ProviderRewardId == rewardId,
                    cancellationToken
                );
            if (!known)
            {
                errors.Add(
                    new(
                        node.Id,
                        "reward-reference-unavailable",
                        "Choose a Custom Reward from this channel."
                    )
                );
            }
        }

        if (
            admission == AutomationGraphAdmission.Saved
            && valid.Configuration is CustomCommandSourceConfiguration command
        )
        {
            await using var commandDb = await dbFactory.CreateDbContextAsync(cancellationToken);
            var known = await commandDb
                .CustomCommands.AsNoTracking()
                .AnyAsync(
                    candidate =>
                        candidate.HostId == hostId.Value && candidate.Id == command.CommandId.Value,
                    cancellationToken
                );
            if (!known)
            {
                errors.Add(
                    new(
                        node.Id,
                        "custom-command-reference-unavailable",
                        "Choose a custom command from this channel."
                    )
                );
            }
        }

        var descriptor = valid.Definition;

        foreach (var (fieldId, binding) in node.InputBindings)
        {
            if (!descriptor.Configuration.Any(field => field.Id == fieldId))
            {
                errors.Add(
                    new(
                        node.Id,
                        "binding-field-invalid",
                        "Select an input from this node.",
                        fieldId
                    )
                );
            }
            else if (
                !Enum.IsDefined(binding.Mode)
                || (
                    binding.Mode == AutomationInputBindingMode.Expression
                    && binding.Expression is null
                )
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "binding-mode-invalid",
                        "Choose Fixed, Connected, or Expression for this input.",
                        fieldId
                    )
                );
            }
            else if (
                binding.Expression is { } expression
                && descriptor.Kind != AutomationNodeKind.Transform
                && !ValidOrdinaryBindingExpression(descriptor, fieldId, expression)
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "binding-expression-invalid",
                        "Enter a valid input expression.",
                        fieldId
                    )
                );
            }
        }
    }

    private bool ValidOrdinaryBindingExpression(
        AutomationDefinitionDescriptor descriptor,
        AutomationConfigurationFieldId fieldId,
        AutomationExpressionSource expression
    )
    {
        if (expression.LanguageVersion != AutomationExpressionLanguage.CurrentVersion)
        {
            return false;
        }

        var input = descriptor.Inputs.SingleOrDefault(port => port.BindingFieldId == fieldId);
        return
            input is { ValueType: AutomationPortValueType.Text }
            && expression.Source.Contains("${", StringComparison.Ordinal)
            ? expressions.ValidateTemplate(expression.Source)
                is not AutomationExpressionCheck.Invalid
            : expressions.Validate(expression) is not AutomationExpressionCheck.Invalid;
    }

    private void ValidateSafeTriggerExpressions(
        AutomationFlowDraft draft,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        var service = new AutomationSafeTriggerExpressionService();
        foreach (var node in draft.Nodes)
        {
            if (
                !definitions.TryGetValue(node.Id, out var definition)
                || definition.Kind != AutomationNodeKind.Transform
            )
            {
                continue;
            }

            foreach (var input in definition.Inputs)
            {
                if (
                    input.BindingFieldId is not { } fieldId
                    || !node.InputBindings.TryGetValue(fieldId, out var binding)
                    || binding.Mode != AutomationInputBindingMode.Expression
                    || binding.Expression is null
                )
                {
                    continue;
                }

                if (!service.Validate(binding.Expression, input, out _, out var invalidSafeField))
                {
                    errors.Add(
                        new(
                            node.Id,
                            "binding-expression-unavailable",
                            "Use only Safe trigger fields available on every Flow path.",
                            fieldId,
                            input.Id,
                            invalidSafeField
                        )
                    );
                }
            }
        }
    }
}
