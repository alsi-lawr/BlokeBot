using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed partial class CustomCommandTemplateRenderer(
    IMessageLibraryRandomSource random,
    IMessageLibraryChatterSource chatters
)
{
    public Task<Result<string, TemplateRenderFailure>> RenderCommandAsync(
        string template,
        MessageLibraryRenderHost host,
        ChatCommandContext context,
        IReadOnlyList<string> args,
        long? count,
        CancellationToken cancellationToken,
        CustomStoredValueSession? storedValues = null
    ) =>
        RenderAsync(
            template,
            host,
            CommandValues(context, args, count),
            cancellationToken,
            storedValues
        );

    private static readonly Regex _storedTokenPattern = new(
        @"\{(?:var|dict)_(?:get|set|inc)(?:\||\}|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    internal static bool ContainsStoredToken(string template) =>
        _storedTokenPattern.IsMatch(template);

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

    public Task<Result<string, TemplateRenderFailure>> RenderScheduledAsync(
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

    private Task<Result<string, TemplateRenderFailure>> RenderAsync(
        string template,
        MessageLibraryRenderHost host,
        IReadOnlyDictionary<string, string> contextualValues,
        CancellationToken cancellationToken,
        CustomStoredValueSession? storedValues = null
    )
    {
        Task<ImmutableArray<HelixChatter>>? chatterLookup = null;
        return RenderAuthoredAsync(
            new(template),
            contextualValues,
            () => chatterLookup ??= chatters.GetAsync(host, cancellationToken),
            cancellationToken,
            storedValues
        );
    }

    private async Task<Result<string, TemplateRenderFailure>> RenderAuthoredAsync(
        AuthoredMessageTemplate authored,
        IReadOnlyDictionary<string, string> contextualValues,
        Func<Task<ImmutableArray<HelixChatter>>> chatterLookup,
        CancellationToken cancellationToken,
        CustomStoredValueSession? storedValues = null
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
                if (
                    IsStoredOperation(
                        MessageLibraryRandomTokenParser.SplitParameters(template[(start + 1)..])[0]
                    )
                )
                {
                    return Result<string, TemplateRenderFailure>.Error(
                        TemplateRenderFailure.StoredSyntax
                    );
                }
                _ = rendered.Append(template, start, template.Length - start);
                break;
            }
            var value = template[(start + 1)..end];
            var parts = MessageLibraryRandomTokenParser.SplitParameters(value);
            var tokenResult = IsStoredOperation(parts[0])
                ? await RenderStoredAsync(parts)
                : await MessageLibraryRandomTokenParser
                    .Parse(value)
                    .Match(
                        token =>
                            RenderRandomAsync(
                                token,
                                contextualValues,
                                chatterLookup,
                                cancellationToken,
                                storedValues
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

            async Task<Result<string, TemplateRenderFailure>> RenderStoredAsync(
                IReadOnlyList<string> parameters
            )
            {
                if (storedValues is null)
                {
                    return Result<string, TemplateRenderFailure>.Error(
                        TemplateRenderFailure.StoredContext
                    );
                }
                var operands = new List<string>();
                foreach (var operand in parameters.Skip(1))
                {
                    var result = await RenderAuthoredAsync(
                        new(operand),
                        contextualValues,
                        chatterLookup,
                        cancellationToken,
                        storedValues
                    );
                    if (
                        !result.Match(
                            text =>
                            {
                                operands.Add(text);
                                return true;
                            },
                            _ => false
                        )
                    )
                    {
                        return result;
                    }
                }
                return storedValues.Apply(parameters[0], operands);
            }

            Task<Result<string, TemplateRenderFailure>> PreservedValueAsync() =>
                Task.FromResult(
                    Result<string, TemplateRenderFailure>.Success(
                        contextualValues.TryGetValue(value, out var contextualValue)
                            ? contextualValue
                            : template[start..(end + 1)]
                    )
                );
        }
        return Result<string, TemplateRenderFailure>.Success(rendered.ToString());
    }

    private Task<Result<string, TemplateRenderFailure>> RenderRandomAsync(
        MessageLibraryRandomToken token,
        IReadOnlyDictionary<string, string> contextualValues,
        Func<Task<ImmutableArray<HelixChatter>>> chatterLookup,
        CancellationToken cancellationToken,
        CustomStoredValueSession? storedValues
    ) =>
        token.Match(
            from =>
                RenderAuthoredAsync(
                    from.Values[random.Next(from.Values.Length)],
                    contextualValues,
                    chatterLookup,
                    cancellationToken,
                    storedValues
                ),
            between =>
                RenderBetweenAsync(
                    between,
                    contextualValues,
                    chatterLookup,
                    cancellationToken,
                    storedValues
                ),
            async _ =>
                Result<string, TemplateRenderFailure>.Success(
                    SelectViewer(await chatterLookup().WaitAsync(cancellationToken))
                )
        );

    private async Task<Result<string, TemplateRenderFailure>> RenderBetweenAsync(
        MessageLibraryRandomToken.Between between,
        IReadOnlyDictionary<string, string> contextualValues,
        Func<Task<ImmutableArray<HelixChatter>>> chatterLookup,
        CancellationToken cancellationToken,
        CustomStoredValueSession? storedValues = null
    )
    {
        var minimum = await RenderBoundAsync(between.Minimum);
        return await minimum.Match(
            async lower =>
            {
                var maximum = await RenderBoundAsync(between.Maximum);
                return maximum.Bind(upper =>
                    lower > upper
                        ? Result<string, TemplateRenderFailure>.Error(
                            TemplateRenderFailure.Reversed
                        )
                        : Result<string, TemplateRenderFailure>.Success(
                            random
                                .NextInclusive(lower, upper)
                                .ToString(CultureInfo.InvariantCulture)
                        )
                );
            },
            error => Task.FromResult(Result<string, TemplateRenderFailure>.Error(error))
        );

        async Task<Result<int, TemplateRenderFailure>> RenderBoundAsync(
            AuthoredMessageTemplate authored
        )
        {
            var result = await RenderAuthoredAsync(
                authored,
                contextualValues,
                chatterLookup,
                cancellationToken,
                storedValues
            );
            return result.Match(ParseBound, Result<int, TemplateRenderFailure>.Error);
        }
    }

    private static Result<int, TemplateRenderFailure> ParseBound(string rendered)
    {
        var value = rendered.Trim();
        if (value.Length == 0)
        {
            return Result<int, TemplateRenderFailure>.Error(TemplateRenderFailure.Missing);
        }
        var digits = value.AsSpan(value[0] is '+' or '-' ? 1 : 0);
        return digits.IsEmpty || digits.ContainsAnyExceptInRange('0', '9')
                ? Result<int, TemplateRenderFailure>.Error(TemplateRenderFailure.NotWholeNumber)
            : MessageLibraryRandomTokenParser.TryParseBound(value, out var number)
                ? Result<int, TemplateRenderFailure>.Success(number)
            : Result<int, TemplateRenderFailure>.Error(TemplateRenderFailure.OutsideRange);
    }

    internal static bool IsStoredOperation(string name) =>
        name.ToLowerInvariant()
            is "var_get"
                or "var_set"
                or "var_inc"
                or "dict_get"
                or "dict_set"
                or "dict_inc";

    private string SelectViewer(ImmutableArray<HelixChatter> available) =>
        available.IsEmpty ? string.Empty : available[random.Next(available.Length)].DisplayName;
}

internal enum TemplateRenderFailure
{
    Missing,
    NotWholeNumber,
    OutsideRange,
    Reversed,
    StoredSyntax,
    StoredContext,
    StoredReference,
    StoredKey,
    StoredNumber,
    StoredOverflow,
    ViewerIdentity,
    InvocationIdentity,
}

internal static class TemplateRenderFailureMessages
{
    public static string ChatMessage(this TemplateRenderFailure failure) =>
        failure switch
        {
            TemplateRenderFailure.StoredSyntax =>
                "Cannot use saved values: check the token's operation, scope and operands.",
            TemplateRenderFailure.StoredContext =>
                "Saved-value tokens require a custom-command viewer context.",
            TemplateRenderFailure.StoredReference =>
                "Cannot use saved values: declare the referenced variable or dictionary in Variables.",
            TemplateRenderFailure.StoredKey =>
                "Cannot use a dictionary: supply a nonempty entry key.",
            TemplateRenderFailure.StoredNumber =>
                "Cannot update a Number value: supply a whole number and check the entry type.",
            TemplateRenderFailure.StoredOverflow =>
                "Cannot increment: the total is outside the signed 64-bit whole-number range.",
            TemplateRenderFailure.ViewerIdentity =>
                "Cannot use User values: the message needs a stable viewer ID.",
            TemplateRenderFailure.InvocationIdentity =>
                "Cannot save values: the message needs a stable invocation ID.",
            TemplateRenderFailure.Missing => "Cannot choose a random number: a bound is missing.",
            TemplateRenderFailure.NotWholeNumber =>
                "Cannot choose a random number: both bounds must be whole numbers.",
            TemplateRenderFailure.OutsideRange =>
                "Cannot choose a random number: bounds must be between -2147483648 and 2147483647.",
            TemplateRenderFailure.Reversed =>
                "Cannot choose a random number: the lower bound must come first.",
        };
}
