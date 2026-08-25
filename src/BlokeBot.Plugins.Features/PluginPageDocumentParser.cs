using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public static partial class PluginPageDocumentParser
{
    public static PluginPageDocumentParseOutcome Parse(
        PluginValue value,
        PluginFeatureDescriptor feature
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(feature);
        var errors = new List<PluginPageDocumentError>();
        if (
            value is not PluginValue.Map root
            || PluginValueValidator.Validate(value) is PluginValueValidationOutcome.Invalid
        )
        {
            return Rejected(PluginPageDocumentErrorCode.InvalidRoot, "$", errors);
        }

        var fields = Fields(root);
        if (!Integer(fields, "version", out var version) || version != 1)
        {
            errors.Add(new(PluginPageDocumentErrorCode.UnsupportedVersion, "$.version"));
        }
        if (!OptionalString(fields, "introduction", out var introduction))
        {
            errors.Add(new(PluginPageDocumentErrorCode.InvalidSchema, "$.introduction"));
        }
        if (!Array(fields, "sections", out var sectionValues))
        {
            errors.Add(new(PluginPageDocumentErrorCode.InvalidSchema, "$.sections"));
            return new PluginPageDocumentParseOutcome.Rejected(errors);
        }
        if (sectionValues.Length > PluginContractLimits.MaximumPageSections)
        {
            errors.Add(new(PluginPageDocumentErrorCode.LimitExceeded, "$.sections"));
        }

        var sections = ImmutableArray.CreateBuilder<PluginPageSection>();
        for (var index = 0; index < sectionValues.Length; index++)
        {
            if (
                ParseSection(sectionValues[index], feature, $"$.sections[{index}]", errors) is
                { } section
            )
            {
                sections.Add(section);
            }
        }
        return errors.Count == 0
            ? new PluginPageDocumentParseOutcome.Parsed(
                new(version, introduction, sections.ToImmutable())
            )
            : new PluginPageDocumentParseOutcome.Rejected(errors);
    }

    private static PluginPageSection? ParseSection(
        PluginValue value,
        PluginFeatureDescriptor feature,
        string location,
        List<PluginPageDocumentError> errors
    )
    {
        if (value is not PluginValue.Map map)
        {
            errors.Add(new(PluginPageDocumentErrorCode.InvalidSchema, location));
            return null;
        }
        var fields = Fields(map);
        if (!String(fields, "kind", out var kind) || !String(fields, "title", out var title))
        {
            errors.Add(new(PluginPageDocumentErrorCode.InvalidSchema, location));
            return null;
        }
        if (!OptionalString(fields, "description", out var description))
        {
            errors.Add(new(PluginPageDocumentErrorCode.InvalidSchema, $"{location}.description"));
            return null;
        }
        return kind switch
        {
            "text" => ParseText(fields, title, location, errors),
            "status" => ParseStatus(fields, title, description, location, errors),
            "form" => ParseForm(fields, feature, title, description, location, errors),
            "table" => ParseTable(fields, title, description, location, errors),
            "list" => ParseList(fields, title, description, location, errors),
            _ => Invalid<PluginPageSection>(location, errors),
        };
    }

    private static PluginPageSection? ParseText(
        IReadOnlyDictionary<string, PluginValue> fields,
        string title,
        string location,
        List<PluginPageDocumentError> errors
    ) =>
        String(fields, "body", out var body)
            ? new PluginPageSection.Text(title, body)
            : Invalid<PluginPageSection>(location, errors);

    private static PluginPageSection? ParseStatus(
        IReadOnlyDictionary<string, PluginValue> fields,
        string title,
        string? description,
        string location,
        List<PluginPageDocumentError> errors
    ) =>
        !String(fields, "tone", out var tone) || !TryTone(tone, out var parsed)
            ? Invalid<PluginPageSection>(location, errors)
            : new PluginPageSection.Status(title, description, parsed);

    private static PluginPageSection? ParseForm(
        IReadOnlyDictionary<string, PluginValue> fields,
        PluginFeatureDescriptor feature,
        string title,
        string? description,
        string location,
        List<PluginPageDocumentError> errors
    )
    {
        if (
            !String(fields, "action", out var actionValue)
            || !PluginActionId.TryCreate(actionValue, out var action)
        )
        {
            return Invalid<PluginPageSection>(location, errors);
        }
        if (!feature.DispatchDeclarations.Actions.Any(candidate => candidate.Id == action))
        {
            errors.Add(new(PluginPageDocumentErrorCode.UnknownAction, $"{location}.action"));
        }
        if (!OptionalString(fields, "submitLabel", out var submitLabel))
        {
            return Invalid<PluginPageSection>(location, errors);
        }
        if (!Array(fields, "fields", out var fieldValues))
        {
            return Invalid<PluginPageSection>(location, errors);
        }
        if (fieldValues.Length > PluginContractLimits.MaximumPageFields)
        {
            errors.Add(new(PluginPageDocumentErrorCode.LimitExceeded, $"{location}.fields"));
        }
        var parsed = ImmutableArray.CreateBuilder<PluginPageField>();
        for (var index = 0; index < fieldValues.Length; index++)
        {
            if (ParseField(fieldValues[index], $"{location}.fields[{index}]", errors) is { } field)
            {
                parsed.Add(field);
            }
        }
        if (
            parsed.Select(static field => field.Id).Distinct(StringComparer.Ordinal).Count()
            != parsed.Count
        )
        {
            errors.Add(new(PluginPageDocumentErrorCode.InvalidSchema, $"{location}.fields"));
        }
        return new PluginPageSection.Form(
            title,
            description,
            action,
            submitLabel ?? "Run action",
            parsed.ToImmutable()
        );
    }

    private static PluginPageField? ParseField(
        PluginValue value,
        string location,
        List<PluginPageDocumentError> errors
    )
    {
        if (value is not PluginValue.Map map)
        {
            return Invalid<PluginPageField>(location, errors);
        }
        var fields = Fields(map);
        if (
            !String(fields, "id", out var id)
            || !ValidLocalId(id)
            || !String(fields, "label", out var label)
            || !String(fields, "kind", out var kindValue)
            || !TryFieldKind(kindValue, out var kind)
        )
        {
            return Invalid<PluginPageField>(location, errors);
        }
        if (
            !OptionalBoolean(fields, "required", out var required)
            || !OptionalString(fields, "help", out var help)
        )
        {
            return Invalid<PluginPageField>(location, errors);
        }
        var choices = ImmutableArray<PluginPageChoice>.Empty;
        if (kind is PluginPageFieldKind.Choice)
        {
            if (
                !Array(fields, "choices", out var values)
                || values.Length > PluginContractLimits.MaximumSettingChoices
            )
            {
                return Invalid<PluginPageField>(location, errors);
            }
            var builder = ImmutableArray.CreateBuilder<PluginPageChoice>();
            foreach (var choice in values)
            {
                if (choice is not PluginValue.Map choiceMap)
                {
                    return Invalid<PluginPageField>(location, errors);
                }
                var choiceFields = Fields(choiceMap);
                if (
                    !String(choiceFields, "value", out var choiceValue)
                    || !String(choiceFields, "label", out var choiceLabel)
                )
                {
                    return Invalid<PluginPageField>(location, errors);
                }
                builder.Add(new(choiceValue, choiceLabel));
            }
            choices = builder.ToImmutable();
        }
        return new(id, label, kind, required, help, choices);
    }
}
