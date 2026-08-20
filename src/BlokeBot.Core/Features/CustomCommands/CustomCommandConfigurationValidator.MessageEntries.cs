namespace BlokeBot.Core.Features.CustomCommands;

public static partial class CustomCommandConfigurationValidator
{
    private static IReadOnlyList<CustomMessageLibraryEntryValue> SnapshotMessageEntries(
        IReadOnlyList<CustomMessageLibraryEntryEditor> editors,
        IReadOnlyList<string> names,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        var values = new List<CustomMessageLibraryEntryValue>(editors.Count);
        for (var entryIndex = 0; entryIndex < editors.Count; entryIndex++)
        {
            var editor = editors[entryIndex];
            if (!Enum.IsDefined(editor.SelectionMode))
            {
                AddError(
                    errors,
                    $"Choose how reply '{names[entryIndex]}' selects messages.",
                    ReplyTarget(editor.Id, CustomCommandValidationFieldKind.SelectionMode)
                );
            }

            if (editor.Variants.Count == 0)
            {
                AddError(
                    errors,
                    $"Reply '{names[entryIndex]}' needs at least one message.",
                    ReplyTarget(editor.Id, CustomCommandValidationFieldKind.VariantText)
                );
            }

            var variants = new List<CustomMessageVariantValue>(editor.Variants.Count);
            foreach (var variant in editor.Variants)
            {
                var text = variant.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    AddError(
                        errors,
                        $"Reply '{names[entryIndex]}' has a blank message.",
                        new(
                            CustomCommandSettingsTab.MessageLibrary,
                            CustomCommandValidationEntityKind.Variant,
                            editor.Id,
                            CustomCommandValidationFieldKind.VariantText,
                            variant.Id
                        )
                    );
                }
                else if (text.Length > _messageVariantMaxLength)
                {
                    AddError(
                        errors,
                        $"Reply messages cannot exceed {_messageVariantMaxLength} characters.",
                        new(
                            CustomCommandSettingsTab.MessageLibrary,
                            CustomCommandValidationEntityKind.Variant,
                            editor.Id,
                            CustomCommandValidationFieldKind.VariantText,
                            variant.Id
                        )
                    );
                }
                else if (MessageLibraryRandomTokenParser.Validate(text) is { } randomTokenError)
                {
                    AddError(
                        errors,
                        randomTokenError,
                        new(
                            CustomCommandSettingsTab.MessageLibrary,
                            CustomCommandValidationEntityKind.Variant,
                            editor.Id,
                            CustomCommandValidationFieldKind.VariantText,
                            variant.Id
                        )
                    );
                }

                variants.Add(new CustomMessageVariantValue(variant.Id, text));
            }

            values.Add(
                new CustomMessageLibraryEntryValue(
                    editor.Id,
                    names[entryIndex],
                    editor.SelectionMode,
                    ClampVariantIndex(editor.CurrentVariantIndex, variants.Count),
                    variants
                )
            );
        }

        return values;
    }

    private static IReadOnlyList<CustomCounterValue> SnapshotCounters(
        IReadOnlyList<CustomCounterEditor> editors,
        IReadOnlyList<string> names
    ) =>
        editors
            .Select(
                (editor, index) => new CustomCounterValue(editor.Id, names[index], editor.Value)
            )
            .ToArray();
}
