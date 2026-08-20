namespace BlokeBot.Core.Features.CustomCommands;

internal static class CustomCommandEditorIds
{
    public static string InventoryLabel(string kind, int id) =>
        $"custom-command-{kind.ToLowerInvariant()}-{id}-inventory-label";

    public static string EditorRegion(string kind, int id) =>
        $"custom-command-{kind.ToLowerInvariant()}-{id}-editor";

    public static string MessageVariant(
        CustomMessageLibraryEntryEditor entry,
        CustomMessageVariantEditor variant
    ) => $"message-entry-{entry.Id}-variant-{variant.Id}";

    public static string CommandAliases(CustomCommandEditor command) =>
        $"command-{command.Id}-aliases";

    public static string MessageEntryName(CustomMessageLibraryEntryEditor entry) =>
        $"message-entry-{entry.Id}-name";

    public static string MessageSelection(CustomMessageLibraryEntryEditor entry) =>
        $"message-entry-{entry.Id}-selection-mode";

    public static string MessageCurrentVariant(CustomMessageLibraryEntryEditor entry) =>
        $"message-entry-{entry.Id}-current-variant";

    public static string AddMessageVariant(CustomMessageLibraryEntryEditor entry) =>
        $"message-entry-{entry.Id}-add-variant";

    public static string CommandName(CustomCommandEditor command) => $"command-{command.Id}-name";

    public static string CommandCooldown(CustomCommandEditor command) =>
        $"command-{command.Id}-cooldown";

    public static string CommandEnabled(CustomCommandEditor command) =>
        $"command-{command.Id}-enabled";

    public static string CommandEveryoneAccess(CustomCommandEditor command) =>
        $"command-{command.Id}-access-everyone";

    public static string CommandRestrictedAccess(CustomCommandEditor command) =>
        $"command-{command.Id}-access-restricted";

    public static string CommandModeratorAccess(CustomCommandEditor command) =>
        $"command-{command.Id}-access-moderators";

    public static string CommandAllowedUser(CustomCommandEditor command) =>
        $"command-{command.Id}-allowed-user";

    public static string CommandCooldownScope(CustomCommandEditor command) =>
        $"command-{command.Id}-cooldown-scope";

    public static string CommandInvocationLimit(CustomCommandEditor command) =>
        $"command-{command.Id}-invocation-limit";

    public static string CommandResetViewer(CustomCommandEditor command) =>
        $"command-{command.Id}-reset-viewer";

    public static string CommandAction(CustomCommandEditor command) =>
        $"command-{command.Id}-action-kind";

    public static string CommandReply(CustomCommandEditor command, int argumentCount) =>
        $"command-{command.Id}-{argumentCount}-argument-reply";

    public static string CommandCounter(CustomCommandEditor command) =>
        $"command-{command.Id}-counter-id";

    public static string CommandOverlayTarget(CustomCommandEditor command) =>
        $"command-{command.Id}-overlay-target";

    public static string CommandOverlayCue(CustomCommandEditor command) =>
        $"command-{command.Id}-overlay-cue";

    public static string CommandQueuePolicy(CustomCommandEditor command) =>
        $"command-{command.Id}-queue-policy";

    public static string CommandReplyOrder(CustomCommandEditor command) =>
        $"command-{command.Id}-reply-order";

    public static string CounterName(CustomCounterEditor counter) => $"counter-{counter.Id}-name";

    public static string CounterValue(CustomCounterEditor counter) => $"counter-{counter.Id}-value";

    public static string AnnouncementName(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-name";

    public static string AnnouncementReply(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-reply";

    public static string AnnouncementEnabled(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-enabled";

    public static string AnnouncementDelivery(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-delivery";

    public static string AnnouncementColor(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-color";

    public static string AnnouncementSchedule(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-schedule-kind";

    public static string AnnouncementRetryDelay(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-retry-delay";

    public static string AnnouncementOccurrenceLifetime(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-occurrence-lifetime";

    public static string AnnouncementInterval(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-interval-minutes";

    public static string AnnouncementChatMessages(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-required-chat-messages";

    public static string AnnouncementDay(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-day";

    public static string AnnouncementWeeklyTime(CustomAnnouncementEditor announcement) =>
        $"announcement-{announcement.Id}-weekly-time";

    public static CustomCommandConfigurationValidationTarget ReplyTarget(
        int replyId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.MessageLibrary,
            CustomCommandValidationEntityKind.Reply,
            replyId,
            field
        );

    public static CustomCommandConfigurationValidationTarget VariantTarget(
        int replyId,
        int variantId
    ) =>
        new(
            CustomCommandSettingsTab.MessageLibrary,
            CustomCommandValidationEntityKind.Variant,
            replyId,
            CustomCommandValidationFieldKind.VariantText,
            variantId
        );

    public static CustomCommandConfigurationValidationTarget CommandTarget(
        int commandId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Command,
            commandId,
            field
        );

    public static CustomCommandConfigurationValidationTarget CounterTarget(
        int counterId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Counter,
            counterId,
            field
        );

    public static CustomCommandConfigurationValidationTarget AnnouncementTarget(
        int announcementId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.ScheduledMessage,
            announcementId,
            field
        );

    public static CustomCommandConfigurationValidationTarget ConfigurationTarget(
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Configuration,
            0,
            field
        );
}
