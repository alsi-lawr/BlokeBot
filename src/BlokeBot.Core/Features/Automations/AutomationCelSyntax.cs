using System.Collections.Immutable;
using System.Text;
using Antlr4.Runtime.Tree;
using Cel;
using Cel.Internal;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationCelSyntax
{
    private static readonly ImmutableHashSet<string> _allowedFunctions = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        AutomationCelTransform.FunctionName,
        "bool",
        "decimal",
        "double",
        "duration",
        "int",
        "size",
        "string",
        "timestamp",
        "type",
        "uint"
    );

    internal static ImmutableHashSet<string> ReservedIdentifiers { get; } =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            AutomationCelTransform.FunctionName,
            "arguments",
            "as",
            "break",
            "const",
            "continue",
            "else",
            "false",
            "for",
            "function",
            "if",
            "import",
            "in",
            "let",
            "loop",
            "null",
            "package",
            "namespace",
            "return",
            "true",
            "var",
            "void",
            "while"
        );

    internal static bool IsIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var tree = new CelEnvironment([], string.Empty).Parse(value);
            return tree.expr()?.GetText() == value
                && tree.expr()
                    .DescendantsAndSelf()
                    .OfType<CelParser.IdentOrGlobalCallContext>()
                    .Count() == 1
                && !value.Contains('(')
                && !value.Contains('.');
        }
        catch (CelException)
        {
            return false;
        }
    }

    internal static AutomationCelIdentifierRewrite RewriteIdentifier(
        string source,
        string from,
        string to
    )
    {
        try
        {
            var occurrences = new CelEnvironment([], string.Empty)
                .Parse(source)
                .DescendantsAndSelf()
                .OfType<CelParser.IdentOrGlobalCallContext>()
                .Where(identifier =>
                    identifier.LPAREN() is null
                    && string.Equals(identifier.id.Text, from, StringComparison.Ordinal)
                )
                .Select(static identifier => identifier.id)
                .OrderBy(static token => token.StartIndex)
                .ToArray();
            if (occurrences.Length == 0)
            {
                return new AutomationCelIdentifierRewrite.Success(source);
            }

            var rewritten = new StringBuilder();
            var offset = 0;
            foreach (var token in occurrences)
            {
                _ = rewritten.Append(source, offset, token.StartIndex - offset).Append(to);
                offset = token.StopIndex + 1;
            }

            return new AutomationCelIdentifierRewrite.Success(
                rewritten.Append(source[offset..]).ToString()
            );
        }
        catch (CelException)
        {
            return new AutomationCelIdentifierRewrite.InvalidSource();
        }
    }

    internal static bool Validate(
        string source,
        IReadOnlyDictionary<string, AutomationCelTransformInput> inputs
    )
    {
        if (!TryAnalyze(source, out var analysis) || analysis.HasCompositeConstructor)
        {
            return false;
        }

        if (!AllowedFunctions(analysis))
        {
            return false;
        }

        foreach (var reference in analysis.References)
        {
            var separator = reference.IndexOf('.');
            var root = separator < 0 ? reference : reference[..separator];
            if (!inputs.TryGetValue(root, out var input))
            {
                return false;
            }

            if (separator >= 0 && !AllowedField(input.ValueType, reference[(separator + 1)..]))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool AllowedFunctions(AutomationCelAnalysis analysis) =>
        analysis.Functions.All(_allowedFunctions.Contains)
        && analysis.MemberFunctions.All(static function =>
            function is "contains" or "endsWith" or "matches" or "size" or "startsWith"
        );

    internal static bool TryAnalyze(string source, out AutomationCelAnalysis analysis)
    {
        analysis = null!;
        try
        {
            var tree = new CelEnvironment([], string.Empty).Parse(source);
            var references = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            var functions = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            var members = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            var composite = Analyze(tree, references, functions, members);
            analysis = new(
                references.ToImmutable(),
                functions.ToImmutable(),
                members.ToImmutable(),
                composite
            );
            return true;
        }
        catch (CelException)
        {
            return false;
        }
    }

    private static bool Analyze(
        IParseTree node,
        ImmutableHashSet<string>.Builder references,
        ImmutableHashSet<string>.Builder functions,
        ImmutableHashSet<string>.Builder members
    )
    {
        if (node is CelParser.SelectContext select && TryPath(select, out var path))
        {
            _ = references.Add(path);
            return false;
        }

        if (node is CelParser.MemberCallContext memberCall)
        {
            _ = members.Add(memberCall.id.Text);
        }

        if (node is CelParser.IdentOrGlobalCallContext identifier)
        {
            if (identifier.LPAREN() is null)
            {
                _ = references.Add(identifier.id.Text);
            }
            else
            {
                _ = functions.Add(identifier.id.Text);
            }
        }

        var composite = node is CelParser.CreateListContext or CelParser.CreateStructContext;
        for (var index = 0; index < node.ChildCount; index++)
        {
            composite |= Analyze(node.GetChild(index), references, functions, members);
        }

        return composite;
    }

    internal static bool TryPath(CelParser.SelectContext select, out string path)
    {
        if (TryMemberPath(select.member(), out var parent))
        {
            path = $"{parent}.{select.id.Text}";
            return true;
        }

        path = string.Empty;
        return false;
    }

    private static bool TryMemberPath(CelParser.MemberContext member, out string path)
    {
        switch (member)
        {
            case CelParser.SelectContext select:
                return TryPath(select, out path);
            case CelParser.PrimaryExprContext primary
                when primary.primary() is CelParser.IdentOrGlobalCallContext identifier
                    && identifier.LPAREN() is null:
                path = identifier.id.Text;
                return true;
            default:
                path = string.Empty;
                return false;
        }
    }

    internal static bool AllowedField(AutomationPortValueType type, string field) =>
        type switch
        {
            AutomationPortValueType.Actor or AutomationPortValueType.Channel => field
                is "login"
                    or "display_name",
            AutomationPortValueType.Stream => field is "title" or "game_name" or "started_at",
            _ => false,
        };
}

internal sealed record AutomationCelAnalysis(
    ImmutableHashSet<string> References,
    ImmutableHashSet<string> Functions,
    ImmutableHashSet<string> MemberFunctions,
    bool HasCompositeConstructor
);

internal abstract record AutomationCelIdentifierRewrite
{
    internal sealed record Success(string Source) : AutomationCelIdentifierRewrite;

    internal sealed record InvalidSource : AutomationCelIdentifierRewrite;
}

internal static class AutomationParseTreeExtensions
{
    internal static IEnumerable<IParseTree> DescendantsAndSelf(this IParseTree tree)
    {
        yield return tree;
        for (var index = 0; index < tree.ChildCount; index++)
        {
            foreach (var descendant in tree.GetChild(index).DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }
}
