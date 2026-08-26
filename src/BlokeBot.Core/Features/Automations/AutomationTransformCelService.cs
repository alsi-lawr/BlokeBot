using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Cel;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class AutomationTransformCelService
{
    private readonly CelEnvironment _environment = CreateEnvironment();

    internal AutomationPureNodeResult Execute(
        AutomationCelTransformConfiguration configuration,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> inputs,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            inputs.Count != configuration.Inputs.Length
            || configuration.Inputs.Any(input => !inputs.ContainsKey(input.PortId))
        )
        {
            return new AutomationPureNodeResult.Failed("input-resolution-failed");
        }

        var bindings = configuration.Inputs.ToDictionary(
            static input => input.Identifier.Value,
            input => ToCelValue(inputs[input.PortId].Value),
            StringComparer.Ordinal
        );
        var provenance = inputs
            .Values.SelectMany(static input => input.Provenance)
            .Append(AutomationValueProvenance.Generated)
            .Distinct()
            .Order()
            .ToImmutableArray();
        var safeTriggerFields = inputs
            .Values.SelectMany(static input =>
                input.SafeTriggerFields.IsDefault ? [] : input.SafeTriggerFields
            )
            .Distinct()
            .OrderBy(static field => field.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var outputs = ImmutableDictionary.CreateBuilder<
            AutomationPortId,
            AutomationResolvedValue
        >();
        try
        {
            foreach (var output in configuration.Outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evaluated = Evaluate(output, bindings);
                if (!TryAutomationValue(output, evaluated, out var value))
                {
                    return new AutomationPureNodeResult.Failed("output-invalid");
                }

                outputs.Add(
                    output.PortId,
                    new(value, provenance, safeTriggerFields, ValueFreeDiagnostic: true)
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (exception is CelException or FormatException)
        {
            return new AutomationPureNodeResult.Failed("output-invalid");
        }

        return new AutomationPureNodeResult.Succeeded(outputs.ToImmutable());
    }

    private object? Evaluate(
        AutomationCelTransformOutput output,
        Dictionary<string, object?> bindings
    )
    {
        var segments = Segments(
            output.Source,
            output.ValueType == AutomationPortValueType.Text
        )!.Value;
        if (
            !output.Source.Contains("${", StringComparison.Ordinal)
            && segments.Length == 1
            && segments[0].Source is { } source
        )
        {
            return _environment.Compile(source)(bindings);
        }

        var text = new StringBuilder();
        foreach (var segment in segments)
        {
            _ = segment.Literal is { } literal
                ? text.Append(literal)
                : text.Append(ToInvariantText(_environment.Compile(segment.Source!)(bindings)));
        }

        return text.ToString();
    }

    private static bool TryAutomationValue(
        AutomationCelTransformOutput output,
        object? evaluated,
        out AutomationValue value
    )
    {
        value = null!;
        if (evaluated is null)
        {
            if (output.Nullability != AutomationPortNullability.Nullable)
            {
                return false;
            }

            value = new AutomationValue.Null(output.ValueType);
            return true;
        }

        value = output.ValueType switch
        {
            AutomationPortValueType.Text when evaluated is string text => new AutomationValue.Text(
                text
            ),
            AutomationPortValueType.Number when evaluated is decimal number =>
                new AutomationValue.Number(number),
            AutomationPortValueType.Number when evaluated is long number =>
                new AutomationValue.Number(number),
            AutomationPortValueType.Number when evaluated is ulong number =>
                new AutomationValue.Number(number),
            AutomationPortValueType.Boolean when evaluated is bool boolean =>
                new AutomationValue.Boolean(boolean),
            AutomationPortValueType.Timestamp when evaluated is DateTimeOffset timestamp =>
                new AutomationValue.Timestamp(timestamp),
            _ => null!,
        };
        return value is not null;
    }

    internal static object? ToCelValue(AutomationValue value) =>
        value switch
        {
            AutomationValue.Text text => text.Value,
            AutomationValue.Number number => number.Value,
            AutomationValue.Boolean boolean => boolean.Value,
            AutomationValue.Timestamp timestamp => timestamp.Value,
            AutomationValue.Actor actor => new Dictionary<string, object?>
            {
                ["login"] = actor.Value.Login,
                ["display_name"] = actor.Value.DisplayName,
            },
            AutomationValue.Channel channel => new Dictionary<string, object?>
            {
                ["login"] = channel.Value.Login,
                ["display_name"] = channel.Value.DisplayName,
            },
            AutomationValue.Stream stream => new Dictionary<string, object?>
            {
                ["title"] = stream.Value.Title,
                ["game_name"] = stream.Value.GameName,
                ["started_at"] = stream.Value.StartedAtUtc,
            },
            AutomationValue.Arguments arguments => arguments
                .Values.OrderBy(static argument => argument.Position)
                .Select(static argument => argument.Value)
                .ToArray(),
            AutomationValue.Array array => array.Items.Select(ToCelValue).ToArray(),
            AutomationValue.Map map => map.Properties.ToDictionary(
                static property => property.Name,
                property => ToCelValue(property.Value),
                StringComparer.Ordinal
            ),
            AutomationValue.Nil => null,
            AutomationValue.Null => null,
            _ => null,
        };

    internal static CelEnvironment CreateEnvironment()
    {
        var environment = new CelEnvironment([], string.Empty);
        environment.RegisterFunction(
            AutomationCelTransform.FunctionName,
            [typeof(decimal)],
            static values => ((decimal)values[0]!).ToString(null, CultureInfo.CurrentCulture)
        );
        environment.RegisterFunction(
            AutomationCelTransform.FunctionName,
            [typeof(decimal), typeof(long)],
            static values =>
            {
                var precision = (long)values[1]!;
                return precision is < 0 or > 6
                    ? throw new CelArgumentRangeException("format_number precision is invalid.")
                    : ((decimal)values[0]!).ToString($"F{precision}", CultureInfo.CurrentCulture);
            }
        );
        return environment;
    }

    private static string ToInvariantText(object? value) =>
        value switch
        {
            null => string.Empty,
            string text => text,
            DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    private sealed record AutomationCelSegment(string? Literal, string? Source);
}
