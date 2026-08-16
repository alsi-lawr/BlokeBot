using System.Collections.Immutable;
using Cel;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationSafeTriggerView
{
    public const int CurrentVersion = 1;

    internal static AutomationSafeTriggerViewField ArgumentsField { get; } =
        new(
            new("arguments"),
            "arguments",
            AutomationPortValueType.Arguments,
            AutomationPortNullability.NonNullable,
            AutomationValueProvenance.PublicChat
        );

    internal static AutomationSafeTriggerViewDescriptor Descriptor { get; } =
        new(CurrentVersion, [ArgumentsField]);
}

internal sealed record AutomationSafeTriggerViewField(
    AutomationSafeTriggerFieldId Id,
    string Path,
    AutomationPortValueType ValueType,
    AutomationPortNullability Nullability,
    AutomationValueProvenance Provenance
);

internal sealed record AutomationSafeTriggerViewDescriptor(
    int Version,
    ImmutableArray<AutomationSafeTriggerViewField> Fields
);

internal sealed class AutomationSafeTriggerExpressionService
{
    private readonly CelEnvironment _environment =
        AutomationTransformCelService.CreateEnvironment();

    internal bool Validate(
        AutomationExpressionSource expression,
        AutomationPortMetadata target,
        out ImmutableArray<AutomationSafeTriggerViewField> references
    ) => Validate(expression, target, out references, out _);

    internal bool Validate(
        AutomationExpressionSource expression,
        AutomationPortMetadata target,
        out ImmutableArray<AutomationSafeTriggerViewField> references,
        out AutomationSafeTriggerFieldId? invalidField
    )
    {
        references = [];
        invalidField = null;
        if (
            expression.LanguageVersion != AutomationExpressionLanguage.CurrentVersion
            || !AutomationCelSyntax.TryAnalyze(expression.Source, out var analysis)
            || analysis.HasCompositeConstructor
            || !AutomationCelSyntax.AllowedFunctions(analysis)
        )
        {
            return false;
        }

        var available = AutomationSafeTriggerView.Descriptor.Fields.ToDictionary(
            static field => field.Path,
            StringComparer.Ordinal
        );
        var resolved = ImmutableArray.CreateBuilder<AutomationSafeTriggerViewField>();
        foreach (var reference in analysis.References.Order(StringComparer.Ordinal))
        {
            if (!available.TryGetValue(reference, out var field))
            {
                invalidField = AutomationSafeTriggerView
                    .Descriptor.Fields.FirstOrDefault(candidate => candidate.Path == reference)
                    ?.Id;
                return false;
            }

            resolved.Add(field);
        }

        try
        {
            _ = _environment.Compile(expression.Source);
        }
        catch (CelException)
        {
            return false;
        }

        if (
            !AutomationCelStaticTypes.TryInfer(
                expression.Source,
                AutomationCelStaticTypes.ForSafeView(),
                out var result
            ) || !result.IsAssignableTo(target.ValueType, target.Nullability)
        )
        {
            return false;
        }

        references = resolved.ToImmutable();
        return true;
    }

    internal AutomationResolvedValue? Evaluate(
        AutomationExpressionSource expression,
        AutomationPortMetadata port,
        AutomationContext context
    )
    {
        if (!Validate(expression, port, out var references))
        {
            return null;
        }

        var bindings = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (references.Any() && !TryBindArguments(bindings, context))
        {
            return null;
        }

        object? evaluated;
        try
        {
            evaluated = _environment.Compile(expression.Source)(bindings);
        }
        catch (CelException)
        {
            return null;
        }

        return !TryValue(evaluated, port, out var value)
            ? null
            : new(
                value,
                references
                    .Select(static field => field.Provenance)
                    .Append(AutomationValueProvenance.Generated)
                    .Distinct()
                    .Order()
                    .ToImmutableArray(),
                [
                    .. references
                        .Select(static field => field.Id)
                        .Distinct()
                        .OrderBy(static field => field.Value, StringComparer.Ordinal),
                ]
            );
    }

    private static bool TryBindArguments(
        IDictionary<string, object?> bindings,
        AutomationContext context
    )
    {
        if (
            context.Arguments.IsDefault
            || context.Arguments.Any(static argument => argument.Position < 0)
            || context.Arguments.Select(static argument => argument.Position).Distinct().Count()
                != context.Arguments.Length
        )
        {
            return false;
        }

        bindings[AutomationSafeTriggerView.ArgumentsField.Path] = context
            .Arguments.OrderBy(static argument => argument.Position)
            .Select(static argument => argument.Value)
            .ToArray();
        return true;
    }

    private static bool TryValue(
        object? evaluated,
        AutomationPortMetadata port,
        out AutomationValue value
    )
    {
        value = null!;
        if (evaluated is null)
        {
            if (port.Nullability != AutomationPortNullability.Nullable)
            {
                return false;
            }

            value = new AutomationValue.Null(port.ValueType);
            return true;
        }

        value = port.ValueType switch
        {
            AutomationPortValueType.Text when evaluated is string text => new AutomationValue.Text(
                text
            ),
            AutomationPortValueType.Number when evaluated is decimal number =>
                new AutomationValue.Number(number),
            AutomationPortValueType.Number when evaluated is long number =>
                new AutomationValue.Number(number),
            AutomationPortValueType.Boolean when evaluated is bool boolean =>
                new AutomationValue.Boolean(boolean),
            AutomationPortValueType.Timestamp when evaluated is DateTimeOffset timestamp =>
                new AutomationValue.Timestamp(timestamp),
            AutomationPortValueType.Arguments when evaluated is IEnumerable<string> arguments =>
                new AutomationValue.Arguments([
                    .. arguments.Select(
                        (argument, position) =>
                            new AutomationValueArgument(
                                position,
                                argument,
                                [AutomationSafeTriggerView.ArgumentsField.Provenance]
                            )
                    ),
                ]),
            _ => null!,
        };
        return value is not null;
    }
}
