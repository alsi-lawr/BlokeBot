namespace BlokeBot.Plugins.Contracts;

public sealed record PluginIdentifierSyntaxContract(
    int MinimumLength,
    int MaximumLength,
    string Separators,
    bool RequiresLowercaseAsciiLetterPrefix,
    bool RequiresLowercaseAsciiLetterOrDigitSuffix,
    bool PermitsAdjacentSeparators
)
{
    public static PluginIdentifierSyntaxContract Current { get; } =
        new(
            MinimumLength: 1,
            MaximumLength: 64,
            Separators: ".-_",
            RequiresLowercaseAsciiLetterPrefix: true,
            RequiresLowercaseAsciiLetterOrDigitSuffix: true,
            PermitsAdjacentSeparators: false
        );
}
