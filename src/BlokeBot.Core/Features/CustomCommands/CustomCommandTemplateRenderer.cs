using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

    public Task<string> RenderCommandAsync(
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

    public Task<string> RenderScheduledAsync(
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

    private async Task<string> RenderAsync(
        string template,
        MessageLibraryRenderHost host,
        IReadOnlyDictionary<string, string> contextualValues,
        CancellationToken cancellationToken
    )
    {
        var rendered = new StringBuilder(template.Length);
        Task<ImmutableArray<HelixChatter>>? chatterLookup = null;
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
            var end = template.IndexOf('}', start + 1);
            if (end < 0)
            {
                _ = rendered.Append(template, start, template.Length - start);
                break;
            }

            var value = template[(start + 1)..end];
            if (MessageLibraryRandomTokenParser.TryParse(value, out var randomToken, out _, out _))
            {
                _ = rendered.Append(
                    await RenderRandomAsync(
                        randomToken!,
                        () => chatterLookup ??= chatters.GetAsync(host, cancellationToken),
                        cancellationToken
                    )
                );
            }
            else if (contextualValues.TryGetValue(value, out var contextualValue))
            {
                _ = rendered.Append(contextualValue);
            }
            else
            {
                _ = rendered.Append(template, start, end - start + 1);
            }

            position = end + 1;
        }

        return rendered.ToString();
    }

    private async Task<string> RenderRandomAsync(
        MessageLibraryRandomToken token,
        Func<Task<ImmutableArray<HelixChatter>>> chatterLookup,
        CancellationToken cancellationToken
    ) =>
        token switch
        {
            MessageLibraryRandomToken.From from => from.Values[random.Next(from.Values.Length)],
            MessageLibraryRandomToken.Between between => random
                .NextInclusive(between.Minimum, between.Maximum)
                .ToString(CultureInfo.InvariantCulture),
            MessageLibraryRandomToken.Viewer => SelectViewer(
                await chatterLookup().WaitAsync(cancellationToken)
            ),
            _ => throw new InvalidOperationException("Unknown random Message Library token."),
        };

    private string SelectViewer(ImmutableArray<HelixChatter> available) =>
        available.IsEmpty ? string.Empty : available[random.Next(available.Length)].DisplayName;
}
