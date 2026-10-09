using System.Collections.Immutable;
using System.Globalization;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed record AuthoredMessageTemplate(string Text);

internal abstract record MessageLibraryRandomToken
{
    private MessageLibraryRandomToken() { }

    public abstract T Match<T>(
        Func<From, T> from,
        Func<Between, T> between,
        Func<Viewer, T> viewer
    );

    internal sealed record From(ImmutableArray<AuthoredMessageTemplate> Values)
        : MessageLibraryRandomToken
    {
        public override T Match<T>(
            Func<From, T> from,
            Func<Between, T> between,
            Func<Viewer, T> viewer
        ) => from(this);
    }

    internal sealed record Between(AuthoredMessageTemplate Minimum, AuthoredMessageTemplate Maximum)
        : MessageLibraryRandomToken
    {
        public override T Match<T>(
            Func<From, T> from,
            Func<Between, T> between,
            Func<Viewer, T> viewer
        ) => between(this);
    }

    internal sealed record Viewer : MessageLibraryRandomToken
    {
        public override T Match<T>(
            Func<From, T> from,
            Func<Between, T> between,
            Func<Viewer, T> viewer
        ) => viewer(this);
    }
}

internal abstract record MessageLibraryTokenParseOutcome
{
    private MessageLibraryTokenParseOutcome() { }

    public abstract T Match<T>(
        Func<MessageLibraryRandomToken, T> parsed,
        Func<string, T> invalid,
        Func<T> unsupported
    );

    internal sealed record Parsed(MessageLibraryRandomToken Token) : MessageLibraryTokenParseOutcome
    {
        public override T Match<T>(
            Func<MessageLibraryRandomToken, T> parsed,
            Func<string, T> invalid,
            Func<T> unsupported
        ) => parsed(Token);
    }

    internal sealed record Invalid(string Error) : MessageLibraryTokenParseOutcome
    {
        public override T Match<T>(
            Func<MessageLibraryRandomToken, T> parsed,
            Func<string, T> invalid,
            Func<T> unsupported
        ) => invalid(Error);
    }

    internal sealed record Unsupported : MessageLibraryTokenParseOutcome
    {
        public override T Match<T>(
            Func<MessageLibraryRandomToken, T> parsed,
            Func<string, T> invalid,
            Func<T> unsupported
        ) => unsupported();
    }
}

internal static class MessageLibraryRandomTokenParser
{
    public static string? Validate(string template) => Validate(template, parameter: false);

    private static string? Validate(string template, bool parameter)
    {
        var position = 0;
        while (position < template.Length)
        {
            if (template[position] == '}' && parameter)
            {
                return "Random message values need balanced braces.";
            }
            if (template[position] != '{')
            {
                position++;
                continue;
            }
            if (!TryFindTokenEnd(template, position, out var end))
            {
                return parameter || IsRecognizedPrefix(template.AsSpan(position + 1))
                    ? "Random message tokens need a closing brace."
                    : null;
            }
            var error = Parse(template[(position + 1)..end])
                .Match<string?>(static _ => null, static invalid => invalid, static () => null);
            if (error is not null)
            {
                return error;
            }
            position = end + 1;
        }
        return null;
    }

    public static bool TryFindTokenEnd(string template, int start, out int end)
    {
        var depth = 1;
        for (end = start + 1; end < template.Length; end++)
        {
            if (template[end] == '{')
            {
                depth++;
            }
            else if (template[end] == '}' && --depth == 0)
            {
                return true;
            }
        }
        return false;
    }

    public static MessageLibraryTokenParseOutcome Parse(string value)
    {
        var parts = SplitParameters(value);
        if (
            !IsName(parts[0], "random_from")
            && !IsName(parts[0], "random_between")
            && !IsName(parts[0], "random_viewer")
        )
        {
            return new MessageLibraryTokenParseOutcome.Unsupported();
        }
        foreach (var part in parts.Skip(1))
        {
            if (Validate(part, parameter: true) is { } error)
            {
                return new MessageLibraryTokenParseOutcome.Invalid(error);
            }
        }
        if (IsName(parts[0], "random_from"))
        {
            var values = parts.Skip(1).Select(static part => part.Trim()).ToImmutableArray();
            return values.IsEmpty || values.Any(string.IsNullOrEmpty)
                ? new MessageLibraryTokenParseOutcome.Invalid(
                    "random_from needs at least one non-empty value."
                )
                : new MessageLibraryTokenParseOutcome.Parsed(
                    new MessageLibraryRandomToken.From(
                        values
                            .Select(static value => new AuthoredMessageTemplate(value))
                            .ToImmutableArray()
                    )
                );
        }
        if (IsName(parts[0], "random_between"))
        {
            if (parts.Length != 3)
            {
                return new MessageLibraryTokenParseOutcome.Invalid(
                    "random_between needs exactly two whole numbers."
                );
            }
            var minimum = parts[1].Trim();
            var maximum = parts[2].Trim();
            var literalMinimum = !minimum.Contains('{', StringComparison.Ordinal);
            var literalMaximum = !maximum.Contains('{', StringComparison.Ordinal);
            var minimumNumber = 0;
            var maximumNumber = 0;
            return (literalMinimum && !TryParseBound(minimum, out minimumNumber))
                || (literalMaximum && !TryParseBound(maximum, out maximumNumber))
                    ? new MessageLibraryTokenParseOutcome.Invalid(
                        "random_between needs exactly two whole numbers."
                    )
                : literalMinimum && literalMaximum && minimumNumber > maximumNumber
                    ? new MessageLibraryTokenParseOutcome.Invalid(
                        "random_between needs the lower number first."
                    )
                : new MessageLibraryTokenParseOutcome.Parsed(
                    new MessageLibraryRandomToken.Between(new(minimum), new(maximum))
                );
        }
        return parts.Length != 1
            ? new MessageLibraryTokenParseOutcome.Invalid("random_viewer does not take values.")
            : new MessageLibraryTokenParseOutcome.Parsed(new MessageLibraryRandomToken.Viewer());
    }

    public static bool TryParseBound(string value, out int number) =>
        int.TryParse(
            value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out number
        );

    internal static ImmutableArray<string> SplitParameters(string value)
    {
        var parts = ImmutableArray.CreateBuilder<string>();
        var depth = 0;
        var start = 0;
        for (var position = 0; position < value.Length; position++)
        {
            if (value[position] == '{')
            {
                depth++;
            }
            else if (value[position] == '}')
            {
                depth--;
            }
            else if (value[position] == '|' && depth == 0)
            {
                parts.Add(value[start..position]);
                start = position + 1;
            }
        }
        parts.Add(value[start..]);
        return parts.ToImmutable();
    }

    private static bool IsRecognizedPrefix(ReadOnlySpan<char> value) =>
        IsTokenNameOrValue(value, "random_from")
        || IsTokenNameOrValue(value, "random_between")
        || IsTokenNameOrValue(value, "random_viewer");

    private static bool IsTokenNameOrValue(ReadOnlySpan<char> value, ReadOnlySpan<char> name) =>
        value.Equals(name, StringComparison.OrdinalIgnoreCase)
        || (
            value.Length > name.Length
            && value[name.Length] == '|'
            && value[..name.Length].Equals(name, StringComparison.OrdinalIgnoreCase)
        );

    private static bool IsName(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
