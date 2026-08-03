using System.Text;
using System.Text.RegularExpressions;

namespace BlokeBot.Core.Features.Overlays;

public sealed record OverlayAppearance
{
    public const int CanvasWidth = 1920;
    public const int CanvasHeight = 1080;
    public const int MinimumWidth = 160;
    public const int MinimumHeight = 90;
    public const int MaximumCssBytes = 4096;

    private static readonly HashSet<string> _selectors =
    [
        ".overlay",
        ".card",
        ".accent",
        ".kicker",
        ".title",
        ".detail",
        ".result",
    ];
    private static readonly HashSet<string> _properties =
    [
        "background",
        "background-color",
        "color",
        "fill",
        "stroke",
        "stroke-width",
        "opacity",
        "font-family",
        "font-size",
        "font-weight",
        "font-style",
        "letter-spacing",
        "text-decoration",
    ];

    public OverlayAppearance(int x, int y, int width, int height, string css)
    {
        if (
            x < 0
            || y < 0
            || width is < MinimumWidth or > CanvasWidth
            || height is < MinimumHeight or > CanvasHeight
            || x + width > CanvasWidth
            || y + height > CanvasHeight
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Appearance must stay inside the 1920 × 1080 canvas and be at least 160 × 90."
            );
        }

        var validation = ValidateCss(css);
        if (validation is not null)
        {
            throw new ArgumentException(validation, nameof(css));
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
        Css = css.Trim();
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public string Css { get; }

    public static OverlayAppearance GuessingDefault => new(160, 690, 1600, 270, string.Empty);

    public static OverlayAppearance GiveawayDefault => new(160, 690, 1600, 270, string.Empty);

    public static OverlayAppearance EventFeedDefault => new(160, 690, 1600, 270, string.Empty);

    public static OverlayAppearance ViewerQueueDefault => new(160, 140, 1200, 800, string.Empty);

    public static string? ValidateCss(string css)
    {
        if (Encoding.UTF8.GetByteCount(css) > MaximumCssBytes)
        {
            return "Advanced styling must be no more than 4096 UTF-8 bytes.";
        }
        if (
            css.Contains('@', StringComparison.Ordinal)
            || Regex.IsMatch(css, @"url\s*\(", RegexOptions.IgnoreCase)
            || css.Contains('<', StringComparison.Ordinal)
            || css.Contains('>', StringComparison.Ordinal)
            || css.Contains('\\', StringComparison.Ordinal)
            || css.Contains("javascript", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(css, @"expression\s*\(", RegexOptions.IgnoreCase)
        )
        {
            return "Advanced styling cannot contain imports, external URLs, markup, scripts, or at-rules.";
        }
        if (string.IsNullOrWhiteSpace(css))
        {
            return null;
        }

        var matches = Regex.Matches(css, @"(?<selector>[^{}]+)\{(?<declarations>[^{}]*)\}");
        var unmatched = Regex.Replace(css, @"(?<selector>[^{}]+)\{(?<declarations>[^{}]*)\}", "");
        if (matches.Count == 0 || !string.IsNullOrWhiteSpace(unmatched))
        {
            return "Advanced styling must contain complete CSS rules without nested or escaping braces.";
        }
        foreach (Match match in matches)
        {
            var selectors = match
                .Groups["selector"]
                .Value.Split(
                    ',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                );
            if (selectors.Length == 0 || selectors.Any(selector => !_selectors.Contains(selector)))
            {
                return "Use only the documented overlay-local selectors: .overlay, .card, .accent, .kicker, .title, .detail, and .result.";
            }
            var declarations = match.Groups["declarations"].Value;
            if (
                string.IsNullOrWhiteSpace(declarations)
                || declarations.Contains('{', StringComparison.Ordinal)
                || declarations.Contains('}', StringComparison.Ordinal)
            )
            {
                return "Each advanced styling rule must contain valid declarations.";
            }
            foreach (
                var declaration in declarations.Split(
                    ';',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                var separator = declaration.IndexOf(':', StringComparison.Ordinal);
                if (
                    separator <= 0
                    || !_properties.Contains(declaration[..separator].Trim().ToLowerInvariant())
                    || string.IsNullOrWhiteSpace(declaration[(separator + 1)..])
                    || declaration[(separator + 1)..].Contains('!', StringComparison.Ordinal)
                )
                {
                    return "Advanced styling supports only safe colour, border, opacity, and typography declarations.";
                }
            }
        }
        return null;
    }

    internal string ToScopedCss() =>
        string.IsNullOrEmpty(Css)
            ? string.Empty
            : Regex.Replace(
                Css,
                @"(?<selector>[^{}]+)\{",
                match =>
                    string.Join(
                        ",",
                        match
                            .Groups["selector"]
                            .Value.Split(
                                ',',
                                StringSplitOptions.TrimEntries
                                    | StringSplitOptions.RemoveEmptyEntries
                            )
                            .Select(selector => $"#overlay-root {selector}")
                    ) + "{"
            );
}
