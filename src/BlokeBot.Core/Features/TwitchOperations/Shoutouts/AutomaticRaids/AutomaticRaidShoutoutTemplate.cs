using System.Text;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

public sealed class AutomaticRaidShoutoutTemplate
{
    public const int MaximumAuthoredCharacters = 150;
    public const int ReservedRuntimeCharacters = 350;
    public const int MaximumRenderedCharacters =
        MaximumAuthoredCharacters + ReservedRuntimeCharacters;

    private readonly Segment[] _segments;

    private AutomaticRaidShoutoutTemplate(string source, Segment[] segments, int authoredCharacters)
    {
        Source = source;
        _segments = segments;
        AuthoredCharacters = authoredCharacters;
    }

    public string Source { get; }
    public int AuthoredCharacters { get; }

    public static AutomaticRaidTemplateParseOutcome Parse(string? source)
    {
        if (source is null)
        {
            return new AutomaticRaidTemplateParseOutcome.Invalid(
                "Enter a shoutout message template."
            );
        }

        var segments = new List<Segment>();
        var literal = new StringBuilder();
        var authoredCharacters = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            if (current == '}')
            {
                return Invalid("A closing brace does not have a matching opening brace.");
            }
            if (current != '{')
            {
                _ = literal.Append(current);
                continue;
            }

            AddLiteral();
            var closing = source.IndexOf('}', index + 1);
            if (closing < 0)
            {
                return Invalid("An opening brace does not have a matching closing brace.");
            }
            var tokenText = source[(index + 1)..closing];
            if (tokenText.Contains('{', StringComparison.Ordinal))
            {
                return Invalid("Nested braces are not supported.");
            }
            if (!TryParseToken(tokenText, out var token, out var error))
            {
                return Invalid(error!);
            }
            segments.Add(token!);
            authoredCharacters += token!.Fallback?.Length ?? 0;
            index = closing;
        }
        AddLiteral();

        return authoredCharacters > MaximumAuthoredCharacters
            ? Invalid(
                $"Template text and fallbacks must be {MaximumAuthoredCharacters} characters or fewer."
            )
            : new AutomaticRaidTemplateParseOutcome.Valid(
                new AutomaticRaidShoutoutTemplate(source, segments.ToArray(), authoredCharacters)
            );
        AutomaticRaidTemplateParseOutcome Invalid(string message) =>
            new AutomaticRaidTemplateParseOutcome.Invalid(message);

        void AddLiteral()
        {
            if (literal.Length == 0)
            {
                return;
            }
            var text = literal.ToString();
            segments.Add(new LiteralSegment(text));
            authoredCharacters += text.Length;
            _ = literal.Clear();
        }
    }

    public AutomaticRaidTemplateRenderOutcome Render(AutomaticRaidTemplateValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var rendered = new StringBuilder();
        foreach (var segment in _segments)
        {
            segment.Append(rendered, values);
        }
        return rendered.Length > MaximumRenderedCharacters
            ? new AutomaticRaidTemplateRenderOutcome.TooLong(
                rendered.Length,
                MaximumRenderedCharacters
            )
            : new AutomaticRaidTemplateRenderOutcome.Rendered(rendered.ToString());
    }

    private static bool TryParseToken(string text, out TokenSegment? token, out string? error)
    {
        token = text switch
        {
            "twitch_handle" => new(TokenKind.TwitchHandle, null),
            "display_name" => new(TokenKind.DisplayName, null),
            "channel_url" => new(TokenKind.ChannelUrl, null),
            "viewer_count" => new(TokenKind.ViewerCount, null),
            _ => null,
        };
        if (token is not null)
        {
            error = null;
            return true;
        }

        var separator = text.IndexOf('|');
        if (separator <= 0)
        {
            error = $"Unknown template token '{{{text}}}'.";
            return false;
        }
        var name = text[..separator];
        var fallback = text[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(fallback))
        {
            error = $"Template token '{name}' requires a non-empty fallback.";
            return false;
        }
        if (fallback.Contains('|', StringComparison.Ordinal))
        {
            error = $"Template token '{name}' has malformed fallback syntax.";
            return false;
        }
        token = name switch
        {
            "last_game" => new(TokenKind.LastGame, fallback),
            "stream_title" => new(TokenKind.StreamTitle, fallback),
            _ => null,
        };
        error = token is null ? $"Unknown template token '{{{text}}}'." : null;
        return token is not null;
    }

    private abstract record Segment
    {
        internal abstract void Append(StringBuilder builder, AutomaticRaidTemplateValues values);
    }

    private sealed record LiteralSegment(string Text) : Segment
    {
        internal override void Append(StringBuilder builder, AutomaticRaidTemplateValues values) =>
            builder.Append(Text);
    }

    private sealed record TokenSegment(TokenKind Kind, string? Fallback) : Segment
    {
        internal override void Append(StringBuilder builder, AutomaticRaidTemplateValues values)
        {
            var value = Kind switch
            {
                TokenKind.TwitchHandle => values.TwitchHandle,
                TokenKind.DisplayName => values.DisplayName,
                TokenKind.ChannelUrl => values.ChannelUrl,
                TokenKind.ViewerCount => values.ViewerCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                TokenKind.LastGame => Optional(values.LastGame),
                TokenKind.StreamTitle => Optional(values.StreamTitle),
                _ => throw new InvalidOperationException("Unsupported template token."),
            };
            _ = builder.Append(value);
        }

        private string Optional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? Fallback! : value;
    }

    private enum TokenKind
    {
        TwitchHandle,
        DisplayName,
        ChannelUrl,
        ViewerCount,
        LastGame,
        StreamTitle,
    }
}

public sealed record AutomaticRaidTemplateValues(
    string TwitchHandle,
    string DisplayName,
    string ChannelUrl,
    int ViewerCount,
    string? LastGame,
    string? StreamTitle
);

public abstract record AutomaticRaidTemplateParseOutcome
{
    private AutomaticRaidTemplateParseOutcome() { }

    public sealed record Valid(AutomaticRaidShoutoutTemplate Template)
        : AutomaticRaidTemplateParseOutcome;

    public sealed record Invalid(string Message) : AutomaticRaidTemplateParseOutcome;
}

public abstract record AutomaticRaidTemplateRenderOutcome
{
    private AutomaticRaidTemplateRenderOutcome() { }

    public sealed record Rendered(string Message) : AutomaticRaidTemplateRenderOutcome;

    public sealed record TooLong(int ActualCharacters, int MaximumCharacters)
        : AutomaticRaidTemplateRenderOutcome;
}
