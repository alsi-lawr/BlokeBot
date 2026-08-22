using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class AutomationConfigurationTransferAdapter
{
    private static AutomationReferenceImportProjection RemapConfiguration(
        AutomationNodeV1 node,
        ConfigurationImportReferencePlan references,
        IReadOnlyList<CommandMatch> commands,
        IReadOnlyList<RewardMatch> rewards,
        bool allowPlannedCommands
    ) =>
        node.DefinitionId switch
        {
            var id when id == AutomationDefinitionIds.CustomCommandSource.Value => RemapCommand(
                node.Configuration,
                references,
                commands,
                allowPlannedCommands
            ),
            var id when id == AutomationDefinitionIds.PlayOverlayCueAction.Value => RemapOverlay(
                node.Configuration,
                references
            ),
            var id when id == AutomationDefinitionIds.RewardRedemptionSource.Value => RemapReward(
                node.Configuration,
                references,
                rewards
            ),
            _ => new(node.Configuration.Clone()),
        };

    private static AutomationReferenceImportProjection RemapCommand(
        JsonElement configuration,
        ConfigurationImportReferencePlan references,
        IReadOnlyList<CommandMatch> commands,
        bool allowPlannedCommands
    )
    {
        if (
            !AutomationReferencePayloadSerializer.TryDeserialize<AutomationCustomCommandTransferPayload>(
                configuration,
                out var payload
            ) || !references.CommandNames.TryGetValue(payload.CustomCommandId, out var name)
        )
        {
            return Placeholder(AutomationTransferPlaceholder.CustomCommand);
        }

        var matches = commands.Where(value => SameName(value.Name, name)).ToArray();
        var commandId = matches.Length switch
        {
            1 => matches[0].Id,
            0 when allowPlannedCommands => int.MaxValue
                - references.CommandNames.Keys.ToList().IndexOf(payload.CustomCommandId),
            _ => 0,
        };
        return commandId > 0
            ? new(
                JsonSerializer.SerializeToElement(
                    new AutomationCustomCommandPersistedPayload(commandId)
                )
            )
            : Placeholder(AutomationTransferPlaceholder.CustomCommand);
    }

    private static AutomationReferenceImportProjection RemapOverlay(
        JsonElement configuration,
        ConfigurationImportReferencePlan references
    ) =>
        (
            !AutomationReferencePayloadSerializer.TryDeserialize<AutomationOverlayTransferPayload>(
                configuration,
                out var payload
            )
            || !references.OverlayInstances.TryGetValue(payload.TargetId, out var targetId)
            || !references.OverlayCues.TryGetValue(payload.CueId, out var cueId)
        )
            ? Placeholder(AutomationTransferPlaceholder.Overlay)
            : new(
                JsonSerializer.SerializeToElement(
                    new AutomationOverlayPersistedPayload(targetId, cueId)
                )
            );

    private static AutomationReferenceImportProjection RemapReward(
        JsonElement configuration,
        ConfigurationImportReferencePlan references,
        IReadOnlyList<RewardMatch> rewards
    )
    {
        if (
            !AutomationReferencePayloadSerializer.TryDeserialize<AutomationRewardTransferPayload>(
                configuration,
                out var payload
            )
        )
        {
            return Placeholder(AutomationTransferPlaceholder.CustomReward);
        }
        if (payload.RewardId is null)
        {
            return new(
                JsonSerializer.SerializeToElement(
                    new AutomationRewardPersistedPayload(null, payload.CompletionPolicy)
                )
            );
        }
        if (!references.RewardNames.TryGetValue(payload.RewardId, out var name))
        {
            return Placeholder(AutomationTransferPlaceholder.CustomReward);
        }
        var matches = rewards.Where(value => SameName(value.Title, name)).ToArray();
        return matches.Length == 1
            ? new(
                JsonSerializer.SerializeToElement(
                    new AutomationRewardPersistedPayload(
                        matches[0].ProviderRewardId,
                        payload.CompletionPolicy
                    )
                )
            )
            : Placeholder(AutomationTransferPlaceholder.CustomReward);
    }

    private static AutomationReferenceImportProjection Placeholder(string reason) =>
        new(AutomationTransferPlaceholder.Create(reason), reason);

    private static bool SameName(string left, string right) =>
        ConfigurationImportReferencePlan.NormalizeName(left)
        == ConfigurationImportReferencePlan.NormalizeName(right);

    private sealed record AutomationReferenceImportProjection(
        JsonElement Configuration,
        string? PlaceholderReason = null
    );

    private sealed record CommandMatch(int Id, string Name);

    private sealed record RewardMatch(string ProviderRewardId, string Title);
}
