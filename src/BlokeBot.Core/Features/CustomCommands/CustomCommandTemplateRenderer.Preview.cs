using System.Text.RegularExpressions;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed partial class CustomCommandTemplateRenderer
{
    private static readonly Regex _contextTokenPattern = new(
        @"\{([A-Za-z0-9_]+)\}",
        RegexOptions.CultureInvariant
    );

    public static string RenderCommandPreview(
        string template,
        ChatCommandContext context,
        IReadOnlyList<string> args,
        long? count,
        CustomStoredValueSession? storedValues = null
    )
    {
        if (!ContainsStoredToken(template))
        {
            var values = CommandValues(context, args, count);
            return _contextTokenPattern.Replace(
                template,
                match =>
                    values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value
            );
        }
        return new CustomCommandTemplateRenderer(
            new PreviewRandomSource(),
            new UnavailableMessageLibraryChatterSource()
        )
            .RenderCommandAsync(
                template,
                new(0, string.Empty, string.Empty),
                context,
                args,
                count,
                CancellationToken.None,
                (
                    storedValues ?? new CustomStoredValueSession([], [], "preview-viewer", true)
                ).Sandbox()
            )
            .GetAwaiter()
            .GetResult()
            .Match(static text => text, static failure => failure.ChatMessage());
    }

    internal static string NameOperand(string name) =>
        name.IndexOfAny(['|', '{', '}']) < 0 ? name : "{arg1}";

    private sealed class PreviewRandomSource : IMessageLibraryRandomSource
    {
        public int Next(int exclusiveMaximum) => 0;

        public int NextInclusive(int minimum, int maximum) => minimum;
    }
}
