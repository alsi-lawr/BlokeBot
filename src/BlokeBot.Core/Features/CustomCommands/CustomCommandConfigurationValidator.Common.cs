namespace BlokeBot.Core.Features.CustomCommands;

public static partial class CustomCommandConfigurationValidator
{
    private static string RequiredName(
        string value,
        string entityName,
        CustomCommandConfigurationValidationTarget target,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            AddError(errors, $"{entityName} name is required.", target);
        }
        else if (trimmed.Length > _nameMaxLength)
        {
            AddError(
                errors,
                $"{entityName} names cannot exceed {_nameMaxLength} characters.",
                target
            );
        }

        return trimmed;
    }

    private static void EnsureUniqueEditorIds(
        IEnumerable<int> ids,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        if (
            ids.GroupBy(static id => id).FirstOrDefault(static group => group.Count() > 1)
            is not null
        )
        {
            AddError(
                errors,
                "Some items were duplicated while you were editing. Reload the page and try again.",
                ConfigurationTarget(CustomCommandValidationFieldKind.Identity)
            );
        }
    }

    private static void EnsureUniqueNames(
        IEnumerable<(string Name, CustomCommandConfigurationValidationTarget Target)> names,
        string entityName,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var duplicate = names
            .Where(static value => !string.IsNullOrWhiteSpace(value.Name))
            .GroupBy(static value => value.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            AddError(
                errors,
                $"Another {entityName} named '{duplicate.Key}' already exists.",
                duplicate.First().Target
            );
        }
    }

    private static int ClampVariantIndex(int index, int variantCount) =>
        variantCount <= 0 ? 0 : Math.Clamp(index, 0, variantCount - 1);

    private static CustomCommandConfigurationValidationTarget ReplyTarget(
        int replyId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.MessageLibrary,
            CustomCommandValidationEntityKind.Reply,
            replyId,
            field
        );

    private static CustomCommandConfigurationValidationTarget CounterTarget(
        int counterId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Counter,
            counterId,
            field
        );

    private static CustomCommandConfigurationValidationTarget CommandTarget(
        int commandId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Command,
            commandId,
            field
        );

    private static CustomCommandConfigurationValidationTarget AnnouncementTarget(
        int announcementId,
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.ScheduledMessage,
            announcementId,
            field
        );

    private static CustomCommandConfigurationValidationTarget ConfigurationTarget(
        CustomCommandValidationFieldKind field
    ) =>
        new(
            CustomCommandSettingsTab.Commands,
            CustomCommandValidationEntityKind.Configuration,
            0,
            field
        );

    private static void AddError(
        ICollection<CustomCommandConfigurationValidationError> errors,
        string message,
        CustomCommandConfigurationValidationTarget target
    ) => errors.Add(new(message, target));
}
