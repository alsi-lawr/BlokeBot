namespace BlokeBot.Core.Features.CustomCommands;

public static partial class CustomCommandConfigurationValidator
{
    private static IReadOnlyList<CustomCommandValue> SnapshotCommands(
        IReadOnlyList<CustomCommandEditor> editors,
        IReadOnlyList<string> names,
        IReadOnlySet<int> messageIds,
        IReadOnlySet<int> counterIds,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var values = new List<CustomCommandValue>(editors.Count);
        for (var index = 0; index < editors.Count; index++)
        {
            var editor = editors[index];
            var replyRoutes = SnapshotReplyRoutes(
                editor.Id,
                names[index],
                editor.Action.ReplyRoutes,
                messageIds,
                editor.Action
                    is MessageCustomCommandActionEditor
                        or CounterCustomCommandActionEditor,
                errors
            );

            if (editor.CooldownSeconds < 0)
            {
                AddError(
                    errors,
                    "The wait between command uses cannot be negative.",
                    CommandTarget(editor.Id, CustomCommandValidationFieldKind.Cooldown)
                );
            }

            if (!Enum.IsDefined(editor.CooldownScope))
            {
                AddError(
                    errors,
                    $"Choose who waits for command '{names[index]}'.",
                    CommandTarget(editor.Id, CustomCommandValidationFieldKind.CooldownScope)
                );
            }

            if (!Enum.IsDefined(editor.InvocationLimit))
            {
                AddError(
                    errors,
                    $"Choose how often command '{names[index]}' can be used.",
                    CommandTarget(editor.Id, CustomCommandValidationFieldKind.InvocationLimit)
                );
            }

            var allowedUsers = SnapshotAllowedUsers(editor, names[index], errors);
            var action = editor.Action switch
            {
                MessageCustomCommandActionEditor => new CustomCommandActionValue.Message(
                    replyRoutes
                ),
                CounterCustomCommandActionEditor counter
                    when counterIds.Contains(counter.CounterId) =>
                    new CustomCommandActionValue.Counter(replyRoutes, counter.CounterId),
                CounterCustomCommandActionEditor counter => MissingCounterAction(
                    editor.Id,
                    names[index],
                    counter,
                    replyRoutes,
                    errors
                ),
                OverlayCueCustomCommandActionEditor cue
                    when cue.TargetOverlayPublicId != Guid.Empty
                        && cue.CuePublicId != Guid.Empty
                        && Enum.IsDefined(cue.QueuePolicy)
                        && Enum.IsDefined(cue.ReplyOrder) =>
                    new CustomCommandActionValue.OverlayCue(
                        replyRoutes,
                        cue.TargetOverlayPublicId,
                        cue.CuePublicId,
                        cue.QueuePolicy,
                        cue.ReplyOrder
                    ),
                OverlayCueCustomCommandActionEditor cue => InvalidOverlayCueAction(
                    editor.Id,
                    names[index],
                    cue,
                    replyRoutes,
                    errors
                ),
                AutomationCustomCommandActionEditor => new CustomCommandActionValue.Automation(
                    replyRoutes
                ),
                _ => InvalidAction(editor.Id, names[index], replyRoutes, errors),
            };
            var aliases = CommandAliasNormalizer.SplitPreservingOrder(editor.Aliases).ToArray();
            if (aliases.Length == 0)
            {
                AddError(
                    errors,
                    "Enter at least one command word.",
                    new(
                        CustomCommandSettingsTab.Commands,
                        CustomCommandValidationEntityKind.Command,
                        editor.Id,
                        CustomCommandValidationFieldKind.Aliases
                    )
                );
            }

            if (aliases.Any(static alias => alias.Length > _aliasMaxLength))
            {
                AddError(
                    errors,
                    $"Command words cannot exceed {_aliasMaxLength} characters.",
                    new(
                        CustomCommandSettingsTab.Commands,
                        CustomCommandValidationEntityKind.Command,
                        editor.Id,
                        CustomCommandValidationFieldKind.Aliases
                    )
                );
            }

            values.Add(
                new CustomCommandValue(
                    editor.Id,
                    names[index],
                    aliases,
                    editor.Enabled,
                    editor.AllowEveryone,
                    editor.AllowModerators,
                    allowedUsers,
                    editor.CooldownSeconds,
                    editor.CooldownScope,
                    editor.InvocationLimit,
                    action
                )
            );
        }

        return values;
    }

    private static IReadOnlyList<CustomCommandAllowedUserValue> SnapshotAllowedUsers(
        CustomCommandEditor editor,
        string commandName,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var values = editor
            .AllowedUsers.Select(user => new CustomCommandAllowedUserValue(
                user.TwitchUserId.Trim(),
                Login.Normalize(user.Login),
                user.DisplayName.Trim()
            ))
            .ToArray();
        if (
            values.Any(user =>
                user.TwitchUserId.Length is 0 or > 128
                || user.Login.Length is 0 or > 128
                || user.DisplayName.Length is 0 or > 128
            )
        )
        {
            AddError(
                errors,
                $"Command '{commandName}' has a selected Twitch account that must be added again.",
                CommandTarget(editor.Id, CustomCommandValidationFieldKind.AllowedUsers)
            );
        }

        if (
            values
                .GroupBy(user => user.TwitchUserId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
        )
        {
            AddError(
                errors,
                $"Command '{commandName}' has the same selected Twitch account more than once.",
                CommandTarget(editor.Id, CustomCommandValidationFieldKind.AllowedUsers)
            );
        }

        return values;
    }

    private static void EnsureUniqueAliases(
        IReadOnlyList<CustomCommandValue> commands,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var duplicate = commands
            .SelectMany(command =>
                command.Aliases.Select(alias => new { Alias = alias, command.Id })
            )
            .GroupBy(value => value.Alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(value => value.Id).Distinct().Count() > 1);
        if (duplicate is not null)
        {
            AddError(
                errors,
                $"!{duplicate.Key} is already used by another custom command.",
                new(
                    CustomCommandSettingsTab.Commands,
                    CustomCommandValidationEntityKind.Command,
                    duplicate.First().Id,
                    CustomCommandValidationFieldKind.Aliases
                )
            );
        }
    }
}
