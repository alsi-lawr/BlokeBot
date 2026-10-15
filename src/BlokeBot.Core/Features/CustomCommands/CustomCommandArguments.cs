using System.Collections.Immutable;
using System.Globalization;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal static class CustomCommandArguments
{
    internal const int MaximumCharacters = 500;
    internal const int MaximumWords = 50;
    private const string _portPrefix = "argument.";

    internal static string PortId(string name) => _portPrefix + name;

    internal static string? Validate(CustomCommandArgumentSchema? schema)
    {
        if (schema is null)
        {
            return null;
        }
        if (schema.Fields.IsDefaultOrEmpty || schema.Fields.Length > MaximumWords)
        {
            return "Add between 1 and 50 required fields.";
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < schema.Fields.Length; index++)
        {
            var field = schema.Fields[index];
            if (field is null)
            {
                return "Choose a supported argument field.";
            }
            if (
                string.IsNullOrEmpty(field.Name)
                || field.Name.Length > 87
                || field.Name[0] is < 'a' or > 'z'
                || field.Name.Any(character =>
                    character
                        is not (>= 'a' and <= 'z')
                            and not (>= '0' and <= '9')
                            and not '.'
                            and not '-'
                )
                || !names.Add(field.Name)
            )
            {
                return "Use unique field names beginning with a lowercase letter, followed by lowercase letters, digits, dots or hyphens (up to 87 characters).";
            }
            if (!Enum.IsDefined(field.Kind))
            {
                return "Choose a supported argument type.";
            }
            if (
                field.Kind == CustomCommandArgumentKind.RemainingText
                && index != schema.Fields.Length - 1
            )
            {
                return "Remaining text must be the last field.";
            }
            if (field.ExclusiveMinimum >= field.Maximum)
            {
                return "The exclusive minimum must be below the maximum.";
            }
            if (
                field.Kind != CustomCommandArgumentKind.Number
                && (field.ExclusiveMinimum is not null || field.Maximum is not null)
            )
            {
                return "Only Number fields have numeric bounds.";
            }
            if (field.MaximumWords is < 1 or > MaximumWords)
            {
                return "Use a word limit from 1 to 50.";
            }
            if (
                field.Kind != CustomCommandArgumentKind.RemainingText
                && field.MaximumWords is not null
            )
            {
                return "Only Remaining text fields have a word limit.";
            }
        }
        return null;
    }

    internal static string OriginalTail(string message)
    {
        var index = 0;
        while (index < message.Length && !char.IsWhiteSpace(message[index]))
        {
            index++;
        }
        return index < message.Length ? message[(index + 1)..] : string.Empty;
    }

    internal static string Usage(string alias, CustomCommandArgumentSchema schema) =>
        $"Usage: !{alias} {string.Join(' ', schema.Fields.Select(field => $"<{field.Name}>"))}";

    internal static CustomCommandArgumentParse Parse(
        int commandId,
        string alias,
        CustomCommandArgumentSchema schema,
        string tail
    )
    {
        var usage = Usage(alias, schema);
        CustomCommandArgumentParse Reject(string reason) =>
            new CustomCommandArgumentParse.Rejected($"{reason} {usage}");
        if (Validate(schema) is not null)
        {
            return Reject("The command's argument configuration needs attention.");
        }
        if (tail.Length > MaximumCharacters || WordCount(tail) > MaximumWords)
        {
            return Reject("Use at most 500 characters and 50 words.");
        }
        var values = ImmutableDictionary.CreateBuilder<string, AutomationValue>(
            StringComparer.Ordinal
        );
        var position = 0;
        foreach (var field in schema.Fields)
        {
            while (position < tail.Length && char.IsWhiteSpace(tail[position]))
            {
                position++;
            }
            if (position == tail.Length)
            {
                return Reject($"Supply {field.Name}.");
            }
            var start = position;
            if (field.Kind == CustomCommandArgumentKind.RemainingText)
            {
                position = tail.Length;
            }
            else
            {
                while (position < tail.Length && !char.IsWhiteSpace(tail[position]))
                {
                    position++;
                }
            }
            var text = tail[start..position];
            if (field.Kind == CustomCommandArgumentKind.Number)
            {
                if (
                    !decimal.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var number
                    )
                )
                {
                    return Reject($"{field.Name} must be an invariant number.");
                }
                if (field.ExclusiveMinimum is { } minimum && number <= minimum)
                {
                    return Reject(
                        $"{field.Name} must be greater than {minimum.ToString(CultureInfo.InvariantCulture)}."
                    );
                }
                if (field.Maximum is { } maximum && number > maximum)
                {
                    return Reject(
                        $"{field.Name} must be at most {maximum.ToString(CultureInfo.InvariantCulture)}."
                    );
                }
                values.Add(field.Name, new AutomationValue.Number(number));
            }
            else
            {
                if (field.MaximumWords is { } limit && WordCount(text) > limit)
                {
                    return Reject(
                        $"Use at most {limit.ToString(CultureInfo.InvariantCulture)} words for {field.Name}."
                    );
                }
                values.Add(field.Name, new AutomationValue.Text(text));
            }
        }
        while (position < tail.Length && char.IsWhiteSpace(tail[position]))
        {
            position++;
        }
        return position != tail.Length
            ? Reject("Remove the extra arguments.")
            : new CustomCommandArgumentParse.Prepared(new(commandId, schema, values.ToImmutable()));
    }

    internal static int WordCount(string text)
    {
        var count = 0;
        var inside = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                inside = false;
            }
            else if (!inside)
            {
                count++;
                inside = true;
            }
        }
        return count;
    }
}

public sealed record AutomationCommandArguments(
    int CommandId,
    CustomCommandArgumentSchema? Schema,
    ImmutableDictionary<string, AutomationValue> Values
);

internal abstract record CustomCommandArgumentParse
{
    private CustomCommandArgumentParse() { }

    internal abstract T Match<T>(Func<Prepared, T> prepared, Func<Rejected, T> rejected);

    internal sealed record Prepared(AutomationCommandArguments Arguments)
        : CustomCommandArgumentParse
    {
        internal override T Match<T>(Func<Prepared, T> prepared, Func<Rejected, T> rejected) =>
            prepared(this);
    }

    internal sealed record Rejected(string Message) : CustomCommandArgumentParse
    {
        internal override T Match<T>(Func<Prepared, T> prepared, Func<Rejected, T> rejected) =>
            rejected(this);
    }
}
