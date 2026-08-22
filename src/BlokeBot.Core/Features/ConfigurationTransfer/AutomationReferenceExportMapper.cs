using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed record AutomationReferenceExportProjection(
    JsonElement Configuration,
    string? PlaceholderReason = null
);

internal static class AutomationReferenceExportMapper
{
    internal static AutomationReferenceExportProjection Map(
        AutomationFlowNode node,
        ConfigurationExportReferencePlan references,
        IDictionary<string, AutomationHostReferenceV1> hostReferences
    )
    {
        var persisted = DeserializeElement(node);
        return persisted.Configuration.ValueKind != JsonValueKind.Object
            ? throw new Format1AutomationConfigurationExportException(
                node.DefinitionId,
                "Automation configuration must be a JSON object."
            )
            : node.DefinitionId switch
            {
                var id when id == AutomationDefinitionIds.CustomCommandSource.Value => MapCommand(
                    node,
                    references,
                    hostReferences
                ),
                var id when id == AutomationDefinitionIds.PlayOverlayCueAction.Value => MapOverlay(
                    node,
                    references,
                    hostReferences
                ),
                var id when id == AutomationDefinitionIds.RewardRedemptionSource.Value => MapReward(
                    node,
                    references,
                    hostReferences
                ),
                _ => persisted,
            };
    }

    private static AutomationReferenceExportProjection MapCommand(
        AutomationFlowNode node,
        ConfigurationExportReferencePlan references,
        IDictionary<string, AutomationHostReferenceV1> hostReferences
    )
    {
        if (
            !AutomationReferencePayloadSerializer.TryDeserializePersisted<AutomationCustomCommandPersistedPayload>(
                node.ConfigurationJson,
                out var payload
            ) || !references.Commands.TryGetValue(payload.CustomCommandId, out var reference)
        )
        {
            return Placeholder(AutomationTransferPlaceholder.CustomCommand);
        }
        AddReference(hostReferences, reference, AutomationHostReferenceKindV1.CustomCommand);
        return new(
            JsonSerializer.SerializeToElement(
                new AutomationCustomCommandTransferPayload(reference.Id)
            )
        );
    }

    private static AutomationReferenceExportProjection MapOverlay(
        AutomationFlowNode node,
        ConfigurationExportReferencePlan references,
        IDictionary<string, AutomationHostReferenceV1> hostReferences
    )
    {
        if (
            !AutomationReferencePayloadSerializer.TryDeserializePersisted<AutomationOverlayPersistedPayload>(
                node.ConfigurationJson,
                out var payload
            )
            || !references.OverlayInstances.TryGetValue(payload.TargetId, out var target)
            || !references.OverlayCues.TryGetValue(payload.CueId, out var cue)
        )
        {
            return Placeholder(AutomationTransferPlaceholder.Overlay);
        }
        AddReference(hostReferences, target, AutomationHostReferenceKindV1.OverlayTarget);
        AddReference(
            hostReferences,
            cue with
            {
                ParentId = target.Id,
            },
            AutomationHostReferenceKindV1.OverlayCue
        );
        return new(
            JsonSerializer.SerializeToElement(
                new AutomationOverlayTransferPayload(target.Id, cue.Id)
            )
        );
    }

    private static AutomationReferenceExportProjection MapReward(
        AutomationFlowNode node,
        ConfigurationExportReferencePlan references,
        IDictionary<string, AutomationHostReferenceV1> hostReferences
    )
    {
        if (
            !AutomationReferencePayloadSerializer.TryDeserializePersisted<AutomationRewardPersistedPayload>(
                node.ConfigurationJson,
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
                    new AutomationRewardTransferPayload(null, payload.CompletionPolicy)
                )
            );
        }
        if (!references.CustomRewards.TryGetValue(payload.RewardId, out var reward))
        {
            return Placeholder(AutomationTransferPlaceholder.CustomReward);
        }
        AddReference(hostReferences, reward, AutomationHostReferenceKindV1.CustomReward);
        return new(
            JsonSerializer.SerializeToElement(
                new AutomationRewardTransferPayload(reward.Id, payload.CompletionPolicy)
            )
        );
    }

    private static AutomationReferenceExportProjection DeserializeElement(AutomationFlowNode node)
    {
        try
        {
            using var document = JsonDocument.Parse(node.ConfigurationJson);
            return new(document.RootElement.Clone());
        }
        catch (JsonException exception)
        {
            throw new Format1AutomationConfigurationExportException(
                node.DefinitionId,
                "Its persisted configuration is not valid JSON.",
                exception
            );
        }
    }

    private static AutomationReferenceExportProjection Placeholder(string reason) =>
        new(AutomationTransferPlaceholder.Create(reason), reason);

    private static void AddReference(
        IDictionary<string, AutomationHostReferenceV1> references,
        ConfigurationExportReference reference,
        AutomationHostReferenceKindV1 kind
    ) => references[reference.Id] = new(reference.Id, kind, reference.Name, reference.ParentId);
}
