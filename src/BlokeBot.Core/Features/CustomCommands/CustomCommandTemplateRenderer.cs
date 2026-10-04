using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed class CustomCommandTemplateRenderer(
    IMessageLibraryRandomSource random,
    IMessageLibraryChatterSource chatters
)
{
    private static readonly Regex _contextTokenPattern = new(
        @"\{([A-Za-z0-9_]+)\}",
        RegexOptions.CultureInvariant
    );

    public Task<Result<string, MessageLibraryNumericBoundFailure>> RenderCommandAsync(
        string template,
        MessageLibraryRenderHost host,
        ChatCommandContext context,
        IReadOnlyList<string> args,
        long? count,
        CancellationToken cancellationToken
    ) => RenderAsync(template, host, CommandValues(context, args, count), cancellationToken);

    public static string RenderCommandPreview(
        string template,
        ChatCommandContext context,
        IReadOnlyList<string> args,
        long? count
    )
    {
        var values = CommandValues(context, args, count);
        return _contextTokenPattern.Replace(
            template,
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value
        );
    }

    private static IReadOnlyDictionary<string, string> CommandValues(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        long? count
    )
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = Login.Normalize(context.Message.Login),
            ["channel"] = Login.Normalize(context.Message.Channel),
            ["command"] = CommandAliasNormalizer.Normalize(context.CommandName),
            ["args"] = string.Join(' ', args),
        };

        for (var i = 0; i < 9; i++)
        {
            values[$"arg{i + 1}"] = i < args.Count ? args[i] : string.Empty;
        }

        if (count is not null)
        {
            values["count"] = count.Value.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    public Task<Result<string, MessageLibraryNumericBoundFailure>> RenderScheduledAsync(
        string template,
        MessageLibraryRenderHost host,
        CancellationToken cancellationToken
    ) =>
        RenderAsync(
            template,
            host,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken
        );

    private Task<Result<string, MessageLibraryNumericBoundFailure>> RenderAsync(
        string template,
        MessageLibraryRenderHost host,
        IReadOnlyDictionary<string, string> contextualValues,
        CancellationToken cancellationToken
    )
    {
        Task<ImmutableArray<HelixChatter>>? chatterLookup = null;
        return RenderAuthoredAsync(
            new(template),
            contextualValues,
            () => chatterLookup ??= chatters.GetAsync(host, cancellationToken),
            cancellationToken
        );
    }

    private async Task<Result<string, MessageLibraryNumericBoundFailure>> RenderAuthoredAsync(
        AuthoredMessageTemplate authored,
        IReadOnlyDictionary<string, string> contextualValues,
        Func<Task<ImmutableArray<HelixChatter>>> chatterLookup,
        CancellationToken cancellationToken
    )
    {
        var template = authored.Text;
        var rendered = new StringBuilder(template.Length);
        var position = 0;
        while (position < template.Length)
        {
            var start = template.IndexOf('{', position);
            if (start < 0)
            {
                _ = rendered.Append(template, position, template.Length - position);
                break;
            }
            _ = rendered.Append(template, position, start - position);
            if (!MessageLibraryRandomTokenParser.TryFindTokenEnd(template, start, out var end))
            {
                _ = rendered.Append(template, start, template.Length - start);
                break;
            }
            var value = template[(start + 1)..end];
            var tokenResult = await MessageLibraryRandomTokenParser
                .Parse(value)
                .Match(
                    token =>
                        RenderRandomAsync(
                            token,
                            contextualValues,
                            chatterLookup,
                            cancellationToken
                        ),
                    _ => PreservedValueAsync(),
                    PreservedValueAsync
                );
            var succeeded = tokenResult.Match(
                text =>
                {
                    _ = rendered.Append(text);
                    return true;
                },
                static _ => false
            );
            if (!succeeded)
            {
                return tokenResult;
            }
            position = end + 1;

            Task<Result<string, MessageLibraryNumericBoundFailure>> PreservedValueAsync() =>
                Task.FromResult(
                    Result<string, MessageLibraryNumericBoundFailure>.Success(
                        contextualValues.TryGetValue(value, out var contextualValue)
                            ? contextualValue
                            : template[start..(end + 1)]
                    )
                );
        }
        return Result<string, MessageLibraryNumericBoundFailure>.Success(rendered.ToString());
    }

    private Task<Result<string, MessageLibraryNumericBoundFailure>> RenderRandomAsync(
        MessageLibraryRandomToken token,
        IReadOnlyDictionary<string, string> contextualValues,
        Func<Task<ImmutableArray<HelixChatter>>> chatterLookup,
        CancellationToken cancellationToken
    ) =>
        token.Match(
            from =>
                RenderAuthoredAsync(
                    from.Values[random.Next(from.Values.Length)],
                    contextualValues,
                    chatterLookup,
                    cancellationToken
                ),
            between =>
                RenderBetweenAsync(between, contextualValues, chatterLookup, cancellationToken),
            async _ =>
                Result<string, MessageLibraryNumericBoundFailure>.Success(
                    SelectViewer(await chatterLookup().WaitAsync(cancellationToken))
                )
        );

    private async Task<Result<string, MessageLibraryNumericBoundFailure>> RenderBetweenAsync(
        MessageLibraryRandomToken.Between between,
        IReadOnlyDictionary<string, string> contextualValues,
        Func<Task<ImmutableArray<HelixChatter>>> chatterLookup,
        CancellationToken cancellationToken
    )
    {
        var minimum = await RenderBoundAsync(between.Minimum);
        return await minimum.Match(
            async lower =>
            {
                var maximum = await RenderBoundAsync(between.Maximum);
                return maximum.Bind(upper =>
                    lower > upper
                        ? Result<string, MessageLibraryNumericBoundFailure>.Error(
                            MessageLibraryNumericBoundFailure.Reversed
                        )
                        : Result<string, MessageLibraryNumericBoundFailure>.Success(
                            random
                                .NextInclusive(lower, upper)
                                .ToString(CultureInfo.InvariantCulture)
                        )
                );
            },
            error => Task.FromResult(Result<string, MessageLibraryNumericBoundFailure>.Error(error))
        );

        async Task<Result<int, MessageLibraryNumericBoundFailure>> RenderBoundAsync(
            AuthoredMessageTemplate authored
        )
        {
            var result = await RenderAuthoredAsync(
                authored,
                contextualValues,
                chatterLookup,
                cancellationToken
            );
            return result.Match(ParseBound, Result<int, MessageLibraryNumericBoundFailure>.Error);
        }
    }

    private static Result<int, MessageLibraryNumericBoundFailure> ParseBound(string rendered)
    {
        var value = rendered.Trim();
        if (value.Length == 0)
        {
            return Result<int, MessageLibraryNumericBoundFailure>.Error(
                MessageLibraryNumericBoundFailure.Missing
            );
        }
        var digits = value.AsSpan(value[0] is '+' or '-' ? 1 : 0);
        return digits.IsEmpty || digits.ContainsAnyExceptInRange('0', '9')
                ? Result<int, MessageLibraryNumericBoundFailure>.Error(
                    MessageLibraryNumericBoundFailure.NotWholeNumber
                )
            : MessageLibraryRandomTokenParser.TryParseBound(value, out var number)
                ? Result<int, MessageLibraryNumericBoundFailure>.Success(number)
            : Result<int, MessageLibraryNumericBoundFailure>.Error(
                MessageLibraryNumericBoundFailure.OutsideRange
            );
    }

    private string SelectViewer(ImmutableArray<HelixChatter> available) =>
        available.IsEmpty ? string.Empty : available[random.Next(available.Length)].DisplayName;
}

internal enum MessageLibraryNumericBoundFailure
{
    Missing,
    NotWholeNumber,
    OutsideRange,
    Reversed,
}

internal static class MessageLibraryNumericBoundFailureMessages
{
    public static string ChatMessage(this MessageLibraryNumericBoundFailure failure) =>
        failure switch
        {
            MessageLibraryNumericBoundFailure.Missing =>
                "Cannot choose a random number: a bound is missing.",
            MessageLibraryNumericBoundFailure.NotWholeNumber =>
                "Cannot choose a random number: both bounds must be whole numbers.",
            MessageLibraryNumericBoundFailure.OutsideRange =>
                "Cannot choose a random number: bounds must be between -2147483648 and 2147483647.",
            MessageLibraryNumericBoundFailure.Reversed =>
                "Cannot choose a random number: the lower bound must come first.",
        };
}
