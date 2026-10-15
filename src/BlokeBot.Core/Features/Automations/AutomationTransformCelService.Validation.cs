using System.Collections.Immutable;
using System.Text;
using Cel;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class AutomationTransformCelService
{
    internal static bool ValidateOutput(
        AutomationCelTransformOutput output,
        IReadOnlyDictionary<string, AutomationCelTransformInput> inputs
    ) => ValidateOutput(output, inputs, out _);

    internal static bool ValidateOutput(
        AutomationCelTransformOutput output,
        IReadOnlyDictionary<string, AutomationCelTransformInput> inputs,
        out AutomationDiagnostic? diagnostic
    )
    {
        diagnostic = null;
        var symbols = AutomationCelStaticTypes.ForInputs(inputs.Values);
        if (
            string.IsNullOrWhiteSpace(output.Source)
            || Segments(output.Source, output.ValueType == AutomationPortValueType.Text)
                is not { } segments
        )
        {
            diagnostic = AutomationDiagnostic.Expression(
                AutomationDiagnosticCategory.Compilation,
                output.Source
            ) with
            {
                Member = new AutomationDiagnosticMember.Output(output.PortId),
            };
            return false;
        }
        var interpolate = output.Source.Contains("${", StringComparison.Ordinal);
        foreach (var segment in segments.Where(static segment => segment.Source is not null))
        {
            var source = segment.Source!;
            diagnostic = AutomationCelSyntax.Diagnose(source, symbols);
            if (diagnostic is null && !CanCompile(source))
            {
                diagnostic = AutomationDiagnostic.Expression(
                    AutomationDiagnosticCategory.Compilation,
                    source
                );
            }
            if (diagnostic is null)
            {
                if (!AutomationCelStaticTypes.TryInfer(source, symbols, out var result))
                {
                    diagnostic = AutomationCelStaticTypes.DiagnoseType(source, symbols);
                }
                else if (
                    interpolate
                        ? output.ValueType != AutomationPortValueType.Text || !result.IsScalarOrNull
                        : !result.IsAssignableTo(output.ValueType, output.Nullability)
                )
                {
                    diagnostic = AutomationDiagnostic.Expression(
                        AutomationDiagnosticCategory.IncompatibleType,
                        source
                    );
                }
            }
            if (diagnostic is not null)
            {
                diagnostic = diagnostic.Offset(segment.Offset) with
                {
                    Member = new AutomationDiagnosticMember.Output(output.PortId),
                };
                return false;
            }
        }
        return true;
    }

    internal static AutomationCelIdentifierRewrite RenameIdentifier(
        AutomationCelTransformOutput output,
        string from,
        string to
    )
    {
        if (
            output.ValueType != AutomationPortValueType.Text
            || !output.Source.Contains("${", StringComparison.Ordinal)
        )
        {
            return AutomationCelSyntax.RewriteIdentifier(output.Source, from, to);
        }

        if (Segments(output.Source, allowInterpolation: true) is not { } segments)
        {
            return new AutomationCelIdentifierRewrite.InvalidSource();
        }

        var rewritten = new StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.Literal is { } literal)
            {
                _ = rewritten.Append(literal);
                continue;
            }

            if (
                AutomationCelSyntax.RewriteIdentifier(segment.Source!, from, to)
                is not AutomationCelIdentifierRewrite.Success success
            )
            {
                return new AutomationCelIdentifierRewrite.InvalidSource();
            }

            _ = rewritten.Append("${").Append(success.Source).Append('}');
        }

        return new AutomationCelIdentifierRewrite.Success(rewritten.ToString());
    }

    private static bool CanCompile(string source)
    {
        try
        {
            _ = CreateEnvironment().Compile(source);
            return true;
        }
        catch (CelException)
        {
            return false;
        }
    }

    private static ImmutableArray<AutomationCelSegment>? Segments(
        string source,
        bool allowInterpolation
    )
    {
        if (!allowInterpolation || !source.Contains("${", StringComparison.Ordinal))
        {
            return [new(null, source)];
        }

        var segments = ImmutableArray.CreateBuilder<AutomationCelSegment>();
        var offset = 0;
        while (offset < source.Length)
        {
            var start = source.IndexOf("${", offset, StringComparison.Ordinal);
            if (start < 0)
            {
                segments.Add(new(source[offset..], null));
                break;
            }

            if (start > offset)
            {
                segments.Add(new(source[offset..start], null));
            }

            var end = source.IndexOf('}', start + 2);
            if (end < 0 || end == start + 2)
            {
                return null;
            }

            segments.Add(new(null, source[(start + 2)..end], start + 2));
            offset = end + 1;
        }

        return segments.ToImmutable();
    }
}
