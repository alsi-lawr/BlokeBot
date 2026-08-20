using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal static partial class CustomCommandConfigurationMapper
{
    public static CustomCommandEditor ToEditor(CustomCommand command) =>
        new()
        {
            Id = command.Id,
            Name = command.Name,
            Aliases = string.Join(
                ", ",
                command
                    .Aliases.OrderBy(static x => x.SortOrder)
                    .ThenBy(static x => x.Id)
                    .Select(static x => x.Alias)
            ),
            Enabled = command.Enabled,
            AllowEveryone = command.AllowEveryone,
            AllowModerators = command.AllowModerators,
            AllowedUsers = command
                .AllowedUsers.OrderBy(static user => user.DisplayName)
                .ThenBy(static user => user.Login)
                .Select(static user => new CustomCommandAllowedUserEditor(
                    user.TwitchUserId,
                    user.Login,
                    user.DisplayName
                ))
                .ToList(),
            CooldownSeconds = command.CooldownSeconds,
            CooldownScope = command.CooldownScope,
            InvocationLimit = command.InvocationLimit,
            Action = command.Action switch
            {
                MessageCustomCommandAction action => new MessageCustomCommandActionEditor
                {
                    ReplyRoutes = ToReplyRoutesEditor(action),
                },
                CounterCustomCommandAction action => new CounterCustomCommandActionEditor
                {
                    ReplyRoutes = ToReplyRoutesEditor(action),
                    CounterId = action.CounterId,
                },
                OverlayCueCustomCommandAction action => new OverlayCueCustomCommandActionEditor
                {
                    ReplyRoutes = ToReplyRoutesEditor(action),
                    TargetOverlayPublicId = action.TargetOverlayPublicId,
                    CuePublicId = action.CuePublicId,
                    QueuePolicy = action.QueuePolicy,
                    ReplyOrder = action.ReplyOrder,
                },
                AutomationCustomCommandAction => new AutomationCustomCommandActionEditor(),
                _ => throw new InvalidOperationException("Unsupported custom command action."),
            },
        };

    public static CustomCommandAction CreateAction(
        int hostId,
        CustomCommandActionValue action,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters
    )
    {
        CustomCommandAction created = action switch
        {
            CustomCommandActionValue.Message => new MessageCustomCommandAction { HostId = hostId },
            CustomCommandActionValue.Counter counter => new CounterCustomCommandAction
            {
                HostId = hostId,
                CounterId = counters[counter.CounterId].Id,
            },
            CustomCommandActionValue.OverlayCue cue => new OverlayCueCustomCommandAction
            {
                HostId = hostId,
                TargetOverlayPublicId = cue.TargetOverlayPublicId,
                CuePublicId = cue.CuePublicId,
                QueuePolicy = cue.QueuePolicy,
                ReplyOrder = cue.ReplyOrder,
            },
            CustomCommandActionValue.Automation => new AutomationCustomCommandAction
            {
                HostId = hostId,
            },
            _ => throw new InvalidOperationException("Unsupported custom command action."),
        };
        ApplyReplyRoutes(created, action.ReplyRoutes, messageEntries);
        return created;
    }

    public static void ApplyAction(
        CustomCommandAction action,
        CustomCommandActionValue value,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters
    )
    {
        ApplyReplyRoutes(action, value.ReplyRoutes, messageEntries);
        if (
            action is CounterCustomCommandAction counterAction
            && value is CustomCommandActionValue.Counter counterValue
        )
        {
            counterAction.CounterId = counters[counterValue.CounterId].Id;
        }
        else if (
            action is OverlayCueCustomCommandAction cueAction
            && value is CustomCommandActionValue.OverlayCue cueValue
        )
        {
            cueAction.TargetOverlayPublicId = cueValue.TargetOverlayPublicId;
            cueAction.CuePublicId = cueValue.CuePublicId;
            cueAction.QueuePolicy = cueValue.QueuePolicy;
            cueAction.ReplyOrder = cueValue.ReplyOrder;
        }
    }

    private static CustomCommandReplyRoutesEditor ToReplyRoutesEditor(CustomCommandAction action) =>
        new()
        {
            ZeroArgumentMessageLibraryEntryId = action.ZeroArgumentMessageLibraryEntryId,
            OneArgumentMessageLibraryEntryId = action.OneArgumentMessageLibraryEntryId,
            TwoArgumentMessageLibraryEntryId = action.TwoArgumentMessageLibraryEntryId,
        };

    private static void ApplyReplyRoutes(
        CustomCommandAction action,
        CustomCommandReplyRoutes routes,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries
    )
    {
        action.ZeroArgumentMessageLibraryEntryId = StoredMessageEntryId(
            routes.ZeroArgumentMessageLibraryEntryId,
            messageEntries
        );
        action.OneArgumentMessageLibraryEntryId = StoredMessageEntryId(
            routes.OneArgumentMessageLibraryEntryId,
            messageEntries
        );
        action.TwoArgumentMessageLibraryEntryId = StoredMessageEntryId(
            routes.TwoArgumentMessageLibraryEntryId,
            messageEntries
        );
    }

    private static int? StoredMessageEntryId(
        int? editorId,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries
    ) => editorId is { } id ? messageEntries[id].Id : null;

    public static bool ActionMatches(CustomCommandAction action, CustomCommandActionValue value) =>
        (action, value)
            is
                (MessageCustomCommandAction, CustomCommandActionValue.Message)
                or
                (CounterCustomCommandAction, CustomCommandActionValue.Counter)
                or
                (OverlayCueCustomCommandAction, CustomCommandActionValue.OverlayCue)
                or
                (AutomationCustomCommandAction, CustomCommandActionValue.Automation);
}
