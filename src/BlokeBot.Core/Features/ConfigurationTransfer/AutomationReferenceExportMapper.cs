using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed record AutomationReferenceExportProjection(
    JsonElement Configuration,
    string? Rejection = null
);

internal static class AutomationReferenceExportMapper
{
    internal static AutomationReferenceExportProjection Map(
        AutomationFlowNode node,
        ConfigurationExportReferencePlan references,
        IDictionary<string, AutomationHostReferenceV2> hostReferences
    )
    {
        var persisted = DeserializeElement(node);
        return persisted.Configuration.ValueKind != JsonValueKind.Object
            ? throw new AutomationConfigurationExportException(
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
        IDictionary<string, AutomationHostReferenceV2> hostReferences
    )
    {
        if (
            !AutomationReferencePayloadSerializer.TryDeserializePersisted<AutomationCustomCommandPersistedPayload>(
                node.ConfigurationJson,
                out var payload
            ) || !references.Commands.TryGetValue(payload.CustomCommandId, out var reference)
        )
        {
            return RejectedReference("custom-command-reference-unavailable");
        }
        AddReference(hostReferences, reference, AutomationHostReferenceKindV2.CustomCommand);
        return new(
            JsonSerializer.SerializeToElement(
                new AutomationCustomCommandTransferPayload(reference.Id)
            )
        );
    }

    private static AutomationReferenceExportProjection MapOverlay(
        AutomationFlowNode node,
        ConfigurationExportReferencePlan references,
        IDictionary<string, AutomationHostReferenceV2> hostReferences
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
            return RejectedReference("overlay-reference-unavailable");
        }
        AddReference(hostReferences, target, AutomationHostReferenceKindV2.OverlayTarget);
        AddReference(
            hostReferences,
            cue with
            {
                ParentId = target.Id,
            },
            AutomationHostReferenceKindV2.OverlayCue
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
        IDictionary<string, AutomationHostReferenceV2> hostReferences
    )
    {
        if (
            !AutomationReferencePayloadSerializer.TryDeserializePersisted<AutomationRewardPersistedPayload>(
                node.ConfigurationJson,
                out var payload
            )
        )
        {
            return RejectedReference("custom-reward-reference-unavailable");
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
            return RejectedReference("custom-reward-reference-unavailable");
        }
        AddReference(hostReferences, reward, AutomationHostReferenceKindV2.CustomReward);
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
            throw new AutomationConfigurationExportException(
                node.DefinitionId,
                "Its persisted configuration is not valid JSON.",
                exception
            );
        }
    }

    private static AutomationReferenceExportProjection RejectedReference(string reason) =>
        new(JsonSerializer.SerializeToElement(new { }), reason);

    private static void AddReference(
        IDictionary<string, AutomationHostReferenceV2> references,
        ConfigurationExportReference reference,
        AutomationHostReferenceKindV2 kind
    ) => references[reference.Id] = new(reference.Id, kind, reference.Name, reference.ParentId);
}
