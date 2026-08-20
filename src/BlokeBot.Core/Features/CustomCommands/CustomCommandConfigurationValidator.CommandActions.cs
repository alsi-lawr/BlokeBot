namespace BlokeBot.Core.Features.CustomCommands;

public static partial class CustomCommandConfigurationValidator
{
    private static CustomCommandActionValue MissingCounterAction(
        int commandId,
        string commandName,
        CounterCustomCommandActionEditor editor,
        CustomCommandReplyRoutes replyRoutes,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        AddError(
            errors,
            $"Choose a counter for command '{commandName}'.",
            CommandTarget(commandId, CustomCommandValidationFieldKind.Counter)
        );
        return new CustomCommandActionValue.Counter(replyRoutes, editor.CounterId);
    }

    private static CustomCommandActionValue InvalidAction(
        int commandId,
        string commandName,
        CustomCommandReplyRoutes replyRoutes,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        AddError(
            errors,
            $"Choose what command '{commandName}' should do.",
            CommandTarget(commandId, CustomCommandValidationFieldKind.Action)
        );
        return new CustomCommandActionValue.Message(replyRoutes);
    }

    private static CustomCommandActionValue InvalidOverlayCueAction(
        int commandId,
        string commandName,
        OverlayCueCustomCommandActionEditor editor,
        CustomCommandReplyRoutes replyRoutes,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        if (editor.TargetOverlayPublicId == Guid.Empty)
        {
            AddError(
                errors,
                $"Choose an overlay player for command '{commandName}'.",
                CommandTarget(commandId, CustomCommandValidationFieldKind.OverlayTarget)
            );
        }
        if (editor.CuePublicId == Guid.Empty)
        {
            AddError(
                errors,
                $"Choose an overlay cue for command '{commandName}'.",
                CommandTarget(commandId, CustomCommandValidationFieldKind.OverlayCue)
            );
        }
        if (!Enum.IsDefined(editor.QueuePolicy))
        {
            AddError(
                errors,
                $"Choose a playback policy for command '{commandName}'.",
                CommandTarget(commandId, CustomCommandValidationFieldKind.QueuePolicy)
            );
        }
        if (!Enum.IsDefined(editor.ReplyOrder))
        {
            AddError(
                errors,
                $"Choose when command '{commandName}' sends its reply.",
                CommandTarget(commandId, CustomCommandValidationFieldKind.ReplyOrder)
            );
        }
        return new CustomCommandActionValue.OverlayCue(
            replyRoutes,
            editor.TargetOverlayPublicId,
            editor.CuePublicId,
            editor.QueuePolicy,
            editor.ReplyOrder
        );
    }

    private static CustomCommandReplyRoutes SnapshotReplyRoutes(
        int commandId,
        string commandName,
        CustomCommandReplyRoutesEditor editor,
        IReadOnlySet<int> messageIds,
        bool replyRequired,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var routes = new (int ArgumentCount, int? MessageEntryId)[]
        {
            (0, editor.ZeroArgumentMessageLibraryEntryId),
            (1, editor.OneArgumentMessageLibraryEntryId),
            (2, editor.TwoArgumentMessageLibraryEntryId),
        };
        if (replyRequired && routes.All(static route => route.MessageEntryId is null))
        {
            AddError(
                errors,
                $"Choose at least one reply for command '{commandName}'.",
                CommandTarget(commandId, CustomCommandValidationFieldKind.ZeroArgumentReply)
            );
        }

        foreach (var route in routes)
        {
            if (route.MessageEntryId is { } messageEntryId && !messageIds.Contains(messageEntryId))
            {
                AddError(
                    errors,
                    $"Choose a saved reply for the {route.ArgumentCount}-argument route on command '{commandName}'.",
                    CommandTarget(commandId, ReplyField(route.ArgumentCount))
                );
            }
        }

        return new(
            editor.ZeroArgumentMessageLibraryEntryId,
            editor.OneArgumentMessageLibraryEntryId,
            editor.TwoArgumentMessageLibraryEntryId
        );
    }

    private static CustomCommandValidationFieldKind ReplyField(int argumentCount) =>
        argumentCount switch
        {
            0 => CustomCommandValidationFieldKind.ZeroArgumentReply,
            1 => CustomCommandValidationFieldKind.OneArgumentReply,
            2 => CustomCommandValidationFieldKind.TwoArgumentReply,
            _ => throw new ArgumentOutOfRangeException(nameof(argumentCount), argumentCount, null),
        };
}
