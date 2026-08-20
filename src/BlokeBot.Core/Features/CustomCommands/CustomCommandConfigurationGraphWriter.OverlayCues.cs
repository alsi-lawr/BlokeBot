using BlokeBot.Core.Features.Overlays;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationGraphWriter
{
    private async Task<CustomCommandConfigurationSaveFailure?> ValidateOverlayCueReferencesAsync(
        int hostId,
        IReadOnlyList<CustomCommandValue> commands,
        CancellationToken ct
    )
    {
        var cueCommands = commands
            .Select(command =>
                (Command: command, Cue: command.Action as CustomCommandActionValue.OverlayCue)
            )
            .Where(value => value.Cue is not null)
            .Select(value => (value.Command, Cue: value.Cue!))
            .ToArray();
        if (cueCommands.Length == 0)
        {
            return null;
        }

        foreach (var (command, cue) in cueCommands)
        {
            var resolution = await overlayCues.ResolveReferencesAsync(
                new(hostId, cue.TargetOverlayPublicId, cue.CuePublicId),
                ct
            );
            if (
                resolution is OverlayCueReferenceOutcome.Disabled
                {
                    Part: OverlayCueReferencePart.Parent,
                }
            )
            {
                return CueFailure(
                    command.Id,
                    CustomCommandValidationFieldKind.OverlayTarget,
                    "Overlays are off. Turn them on in Channel setup before changing an overlay cue command."
                );
            }

            if (
                resolution
                is OverlayCueReferenceOutcome.Missing
                    {
                        Part: OverlayCueReferencePart.Parent or OverlayCueReferencePart.Target,
                    }
                    or OverlayCueReferenceOutcome.Disabled { Part: OverlayCueReferencePart.Target }
            )
            {
                return CueFailure(
                    command.Id,
                    CustomCommandValidationFieldKind.OverlayTarget,
                    "The selected overlay player is unavailable, disabled, deleted, or belongs to another channel."
                );
            }

            if (resolution is not OverlayCueReferenceOutcome.Available)
            {
                return CueFailure(
                    command.Id,
                    CustomCommandValidationFieldKind.OverlayCue,
                    "The selected overlay cue is unavailable, disabled, deleted, or belongs to another channel."
                );
            }
        }
        return null;
    }

    private static CustomCommandConfigurationSaveFailure CueFailure(
        int commandId,
        CustomCommandValidationFieldKind field,
        string message
    ) => new CustomCommandConfigurationSaveFailure.OverlayCueReference(commandId, field, message);
}
