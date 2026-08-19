using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Antlr4.Runtime.Tree;
using Cel;
using Cel.Internal;

namespace BlokeBot.Core.Features.Automations;

internal sealed record AutomationCelTransformInput(
    AutomationPortId PortId,
    AutomationCelIdentifier Identifier,
    string DisplayName,
    AutomationConfigurationFieldId BindingFieldId,
    AutomationPortValueType ValueType,
    AutomationPortNullability Nullability,
    AutomationValue FixedValue
);

internal sealed record AutomationCelTransformOutput(
    AutomationPortId PortId,
    string DisplayName,
    AutomationPortValueType ValueType,
    AutomationPortNullability Nullability,
    string Source
);

internal sealed record AutomationCelTransformConfiguration(
    ImmutableArray<AutomationCelTransformInput> Inputs,
    ImmutableArray<AutomationCelTransformOutput> Outputs
) : AutomationConfiguration;

internal static class AutomationCelTransform
{
    internal const string FunctionName = "format_number";

    internal static IAutomationDefinition Definition(
        AutomationDefinitionId id,
        AutomationDisplayMetadata display
    ) =>
        new AutomationDefinition<AutomationCelTransformConfiguration>(
            new(
                id,
                AutomationNodeKind.Transform,
                AutomationDefinitionScope.Host,
                new(new(1), new(1)),
                display,
                [],
                [],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            Parse,
            Validate,
            configuration => Descriptor(id, display, configuration)
        );

    internal static AutomationPureHandlerContract HandlerContract(AutomationDefinitionId id) =>
        new(id, AutomationNodeKind.Transform, [], [], UsesEffectiveDescriptor: true);

    private static AutomationConfigurationParseResult Parse(JsonElement json)
    {
        if (
            json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("inputs", out var inputsJson)
            || inputsJson.ValueKind != JsonValueKind.Array
            || !json.TryGetProperty("outputs", out var outputsJson)
            || outputsJson.ValueKind != JsonValueKind.Array
        )
        {
            return Invalid("schema", "Declare the Transform inputs and outputs.");
        }

        var inputs = ImmutableArray.CreateBuilder<AutomationCelTransformInput>();
        foreach (var inputJson in inputsJson.EnumerateArray())
        {
            if (!TryParseInput(inputJson, out var input))
            {
                return Invalid("schema", "Repair the persisted Transform input schema.");
            }

            inputs.Add(input);
        }

        var outputs = ImmutableArray.CreateBuilder<AutomationCelTransformOutput>();
        foreach (var outputJson in outputsJson.EnumerateArray())
        {
            if (!TryParseOutput(outputJson, out var output))
            {
                return Invalid("schema", "Repair the persisted Transform output schema.");
            }

            outputs.Add(output);
        }

        return new AutomationConfigurationParseResult.Parsed(
            new AutomationCelTransformConfiguration(inputs.ToImmutable(), outputs.ToImmutable())
        );
    }

    private static AutomationValidationResult Validate(
        AutomationCelTransformConfiguration configuration
    )
    {
        if (configuration.Outputs.IsEmpty)
        {
            return InvalidResult("schema", "Declare at least one Transform output.");
        }

        if (
            HasDuplicates(configuration.Inputs.Select(static input => input.PortId.Value))
            || HasDuplicates(configuration.Inputs.Select(static input => input.Identifier.Value))
            || HasDuplicates(
                configuration.Inputs.Select(static input => input.BindingFieldId.Value)
            )
            || HasDuplicates(configuration.Outputs.Select(static output => output.PortId.Value))
            || configuration
                .Inputs.Select(static input => input.PortId)
                .Intersect(configuration.Outputs.Select(static output => output.PortId))
                .Any()
        )
        {
            return InvalidResult("schema", "Use unique, non-reused Transform identities.");
        }

        foreach (var input in configuration.Inputs)
        {
            if (
                string.IsNullOrWhiteSpace(input.DisplayName)
                || !AutomationCelSyntax.IsIdentifier(input.Identifier.Value)
                || AutomationCelSyntax.ReservedIdentifiers.Contains(input.Identifier.Value)
                || input.ValueType == AutomationPortValueType.Flow
                || !Enum.IsDefined(input.ValueType)
                || !Enum.IsDefined(input.Nullability)
                || !Matches(input.ValueType, input.Nullability, input.FixedValue)
            )
            {
                return InvalidResult(
                    input.BindingFieldId.Value,
                    "Repair this Transform input declaration."
                );
            }
        }

        var declaredInputs = configuration.Inputs.ToImmutableDictionary(
            static input => input.Identifier.Value,
            StringComparer.Ordinal
        );
        foreach (var output in configuration.Outputs)
        {
            if (
                string.IsNullOrWhiteSpace(output.DisplayName)
                || !Scalar(output.ValueType)
                || !Enum.IsDefined(output.Nullability)
                || string.IsNullOrWhiteSpace(output.Source)
                || !AutomationTransformCelService.ValidateOutput(output, declaredInputs)
            )
            {
                return AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Port(output.PortId),
                    "Repair this Transform output expression."
                );
            }
        }

        return AutomationValidationResult.Valid;
    }

    private static AutomationDefinitionDescriptor Descriptor(
        AutomationDefinitionId id,
        AutomationDisplayMetadata display,
        AutomationCelTransformConfiguration configuration
    ) =>
        new(
            id,
            AutomationNodeKind.Transform,
            AutomationDefinitionScope.Host,
            new(new(1), new(1)),
            display,
            [
                .. configuration.Inputs.Select(static input => new AutomationPortMetadata(
                    input.PortId,
                    input.DisplayName,
                    "Receives an exact typed Transform input.",
                    input.ValueType,
                    Nullability: input.Nullability,
                    BindingFieldId: input.BindingFieldId
                )),
            ],
            [
                .. configuration.Outputs.Select(static output => new AutomationPortMetadata(
                    output.PortId,
                    output.DisplayName,
                    "Supplies an exact typed Transform result.",
                    output.ValueType,
                    Nullability: output.Nullability
                )),
            ],
            [
                .. configuration.Inputs.Select(
                    static input => new AutomationConfigurationFieldMetadata(
                        input.BindingFieldId,
                        input.DisplayName,
                        "Retains the Transform input binding payload.",
                        new AutomationConfigurationFieldType.Data(input.ValueType),
                        input.Nullability == AutomationPortNullability.NonNullable
                    )
                ),
            ],
            AutomationActionCapabilities.None,
            AutomationActionRetrySafety.NotApplicable
        );

    private static bool TryParseInput(JsonElement json, out AutomationCelTransformInput input)
    {
        input = null!;
        if (
            !TryString(json, "port-id", out var portId)
            || !TryString(json, "cel-identifier", out var identifier)
            || !TryString(json, "display-name", out var displayName)
            || !TryString(json, "binding-field-id", out var bindingFieldId)
            || !TryType(json, out var valueType)
            || !TryNullability(json, out var nullability)
            || !json.TryGetProperty("fixed", out var fixedJson)
            || !TryValue(fixedJson, valueType, nullability, out var fixedValue)
        )
        {
            return false;
        }

        input = new(
            new(portId),
            new(identifier),
            displayName,
            new(bindingFieldId),
            valueType,
            nullability,
            fixedValue
        );
        return true;
    }

    private static bool TryParseOutput(JsonElement json, out AutomationCelTransformOutput output)
    {
        output = null!;
        if (
            !TryString(json, "port-id", out var portId)
            || !TryString(json, "display-name", out var displayName)
            || !TryType(json, out var valueType)
            || !TryNullability(json, out var nullability)
            || !TryString(json, "cel", out var source)
        )
        {
            return false;
        }

        output = new(new(portId), displayName, valueType, nullability, source);
        return true;
    }

    private static bool TryValue(
        JsonElement json,
        AutomationPortValueType type,
        AutomationPortNullability nullability,
        out AutomationValue value
    )
    {
        value = null!;
        if (json.ValueKind == JsonValueKind.Null)
        {
            if (nullability != AutomationPortNullability.Nullable)
            {
                return false;
            }

            value = new AutomationValue.Null(type);
            return true;
        }

        AutomationValue? parsed = type switch
        {
            AutomationPortValueType.Text when json.ValueKind == JsonValueKind.String =>
                new AutomationValue.Text(json.GetString()!),
            AutomationPortValueType.Number when json.TryGetDecimal(out var number) =>
                new AutomationValue.Number(number),
            AutomationPortValueType.Boolean
                when json.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                new AutomationValue.Boolean(json.GetBoolean()),
            AutomationPortValueType.Timestamp
                when json.ValueKind == JsonValueKind.String
                    && json.TryGetDateTimeOffset(out var timestamp) =>
                new AutomationValue.Timestamp(timestamp),
            AutomationPortValueType.Actor => TryActor(json),
            AutomationPortValueType.Channel => TryChannel(json),
            AutomationPortValueType.Stream => TryStream(json),
            AutomationPortValueType.Arguments => TryArguments(json),
            _ => null,
        };
        if (parsed is null)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static AutomationValue? TryActor(JsonElement json) =>
        TryString(json, "login", out var login)
        && TryString(json, "display-name", out var displayName)
            ? new AutomationValue.Actor(new(login, displayName))
            : null;

    private static AutomationValue? TryChannel(JsonElement json) =>
        TryString(json, "login", out var login)
        && TryString(json, "display-name", out var displayName)
            ? new AutomationValue.Channel(new(login, displayName))
            : null;

    private static AutomationValue? TryStream(JsonElement json) =>
        json.ValueKind != JsonValueKind.Object
        || !TryOptionalString(json, "title", out var title)
        || !TryOptionalString(json, "game-name", out var gameName)
        || !TryOptionalTimestamp(json, "started-at", out var startedAt)
            ? null
            : new AutomationValue.Stream(new(title, gameName, startedAt));

    private static AutomationValue? TryArguments(JsonElement json) =>
        json.ValueKind == JsonValueKind.Array
        && json.EnumerateArray().All(static value => value.ValueKind == JsonValueKind.String)
            ? new AutomationValue.Arguments([
                .. json.EnumerateArray()
                    .Select(
                        static (value, position) =>
                            new AutomationValueArgument(
                                position,
                                value.GetString()!,
                                [AutomationValueProvenance.Generated]
                            )
                    ),
            ])
            : null;

    private static bool TryOptionalString(JsonElement json, string name, out string? result)
    {
        result = null;
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = value.GetString();
        return true;
    }

    private static bool TryOptionalTimestamp(
        JsonElement json,
        string name,
        out DateTimeOffset? result
    )
    {
        result = null;
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (
            value.ValueKind != JsonValueKind.String
            || !value.TryGetDateTimeOffset(out var timestamp)
        )
        {
            return false;
        }

        result = timestamp;
        return true;
    }

    private static bool TryString(JsonElement json, string name, out string value)
    {
        value = string.Empty;
        if (
            json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static bool TryType(JsonElement json, out AutomationPortValueType value) =>
        TryEnum(json, "type", out value);

    private static bool TryNullability(JsonElement json, out AutomationPortNullability value) =>
        TryEnum(json, "nullability", out value);

    private static bool TryEnum<T>(JsonElement json, string name, out T value)
        where T : struct, Enum
    {
        value = default;
        return TryString(json, name, out var text)
            && Enum.TryParse(text, out value)
            && Enum.IsDefined(value)
            && text == value.ToString();
    }

    private static bool HasDuplicates(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Count() != values.Count();

    private static bool Scalar(AutomationPortValueType type) =>
        type
            is AutomationPortValueType.Text
                or AutomationPortValueType.Number
                or AutomationPortValueType.Boolean
                or AutomationPortValueType.Timestamp;

    private static bool Matches(
        AutomationPortValueType type,
        AutomationPortNullability nullability,
        AutomationValue value
    ) =>
        value switch
        {
            AutomationValue.Null nullValue => nullability == AutomationPortNullability.Nullable
                && nullValue.ValueType == type,
            _ => AutomationPureHandlerRegistry.ValueType(value) == type,
        };

    private static AutomationConfigurationParseResult Invalid(string fieldId, string message) =>
        new AutomationConfigurationParseResult.Invalid([
            new(new AutomationValidationTarget.Field(new(fieldId)), message),
        ]);

    private static AutomationValidationResult InvalidResult(string fieldId, string message) =>
        AutomationValidationResult.Invalid(
            new AutomationValidationTarget.Field(new(fieldId)),
            message
        );
}

internal sealed class AutomationTransformCelService
{
    private readonly CelEnvironment _environment = CreateEnvironment();

    internal static bool ValidateOutput(
        AutomationCelTransformOutput output,
        IReadOnlyDictionary<string, AutomationCelTransformInput> inputs
    )
    {
        if (
            Segments(output.Source, output.ValueType == AutomationPortValueType.Text)
            is not { } segments
        )
        {
            return false;
        }

        var expressions = segments
            .Where(static segment => segment.Source is not null)
            .Select(static segment => segment.Source!)
            .ToArray();
        if (
            expressions.Any(source =>
                !AutomationCelSyntax.Validate(source, inputs) || !CanCompile(source)
            )
        )
        {
            return false;
        }

        var symbols = AutomationCelStaticTypes.ForInputs(inputs.Values);
        return output.Source.Contains("${", StringComparison.Ordinal)
            ? output.ValueType == AutomationPortValueType.Text
                && expressions.All(source =>
                    AutomationCelStaticTypes.TryInfer(source, symbols, out var type)
                    && type.IsScalarOrNull
                )
            : expressions.Length == 1
                && AutomationCelStaticTypes.TryInfer(expressions[0], symbols, out var result)
                && result.IsAssignableTo(output.ValueType, output.Nullability);
    }

    internal static string RenameIdentifier(
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
            return output.Source;
        }

        var rewritten = new StringBuilder();
        foreach (var segment in segments)
        {
            _ = segment.Literal is { } literal
                ? rewritten.Append(literal)
                : rewritten
                    .Append("${")
                    .Append(AutomationCelSyntax.RewriteIdentifier(segment.Source!, from, to))
                    .Append('}');
        }

        return rewritten.ToString();
    }

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

            segments.Add(new(null, source[(start + 2)..end]));
            offset = end + 1;
        }

        return segments.ToImmutable();
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

internal sealed class AutomationCelTransformHandler : IAutomationPureNodeHandler
{
    public AutomationCelTransformHandler()
        : this(AutomationDefinitionIds.CelTransform) { }

    internal AutomationCelTransformHandler(AutomationDefinitionId definitionId) =>
        Contract = AutomationCelTransform.HandlerContract(definitionId);

    private readonly AutomationTransformCelService _service = new();
    private int _calls;

    public AutomationPureHandlerContract Contract { get; }

    internal int Calls => Volatile.Read(ref _calls);

    public ValueTask<AutomationPureNodeResult> ExecuteAsync(
        AutomationPureNodeInput input,
        CancellationToken cancellationToken
    )
    {
        _ = Interlocked.Increment(ref _calls);
        return ValueTask.FromResult(
            input.Configuration is AutomationCelTransformConfiguration configuration
                ? _service.Execute(configuration, input.Inputs, cancellationToken)
                : new AutomationPureNodeResult.Failed("configuration-invalid")
        );
    }
}

internal enum AutomationCelNumberKind
{
    None,
    Decimal,
    Integer,
    UnsignedInteger,
    Double,
}

internal readonly record struct AutomationCelStaticType(
    AutomationPortValueType? ValueType,
    bool CanBeNull,
    AutomationCelNumberKind NumberKind = AutomationCelNumberKind.None,
    long? IntegerConstant = null
)
{
    internal bool IsScalarOrNull =>
        ValueType
            is null
                or AutomationPortValueType.Text
                or AutomationPortValueType.Number
                or AutomationPortValueType.Boolean
                or AutomationPortValueType.Timestamp;

    internal bool IsAssignableTo(
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    ) =>
        (ValueType == valueType || (ValueType is null && CanBeNull))
        && (nullability == AutomationPortNullability.Nullable || !CanBeNull)
        && (
            valueType != AutomationPortValueType.Number
            || NumberKind != AutomationCelNumberKind.Double
        );
}

internal static class AutomationCelStaticTypes
{
    internal static IReadOnlyDictionary<string, AutomationCelStaticType> ForInputs(
        IEnumerable<AutomationCelTransformInput> inputs
    )
    {
        var symbols = new Dictionary<string, AutomationCelStaticType>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            AddSymbol(symbols, input.Identifier.Value, input.ValueType, input.Nullability);
        }

        return symbols;
    }

    internal static IReadOnlyDictionary<string, AutomationCelStaticType> ForSafeView()
    {
        var symbols = new Dictionary<string, AutomationCelStaticType>(StringComparer.Ordinal);
        foreach (var field in AutomationSafeTriggerView.Descriptor.Fields)
        {
            AddSymbol(symbols, field.Path, field.ValueType, field.Nullability);
        }

        return symbols;
    }

    internal static bool TryInfer(
        string source,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        try
        {
            return TryInfer(
                new CelEnvironment([], string.Empty).Parse(source).expr(),
                symbols,
                out type
            );
        }
        catch (CelException)
        {
            return false;
        }
    }

    private static void AddSymbol(
        IDictionary<string, AutomationCelStaticType> symbols,
        string name,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    )
    {
        symbols.Add(name, From(valueType, nullability));
        var parentNullable = nullability == AutomationPortNullability.Nullable;
        switch (valueType)
        {
            case AutomationPortValueType.Actor:
            case AutomationPortValueType.Channel:
                symbols.Add($"{name}.login", Text(parentNullable));
                symbols.Add($"{name}.display_name", Text(parentNullable));
                break;
            case AutomationPortValueType.Stream:
                symbols.Add($"{name}.title", Text(canBeNull: true));
                symbols.Add($"{name}.game_name", Text(canBeNull: true));
                symbols.Add(
                    $"{name}.started_at",
                    new(AutomationPortValueType.Timestamp, CanBeNull: true)
                );
                break;
        }
    }

    private static AutomationCelStaticType From(
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    ) =>
        new(
            valueType,
            nullability == AutomationPortNullability.Nullable,
            valueType == AutomationPortValueType.Number
                ? AutomationCelNumberKind.Decimal
                : AutomationCelNumberKind.None
        );

    private static AutomationCelStaticType Text(bool canBeNull = false) =>
        new(AutomationPortValueType.Text, canBeNull);

    private static AutomationCelStaticType Boolean() =>
        new(AutomationPortValueType.Boolean, CanBeNull: false);

    private static bool TryInfer(
        CelParser.ExprContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        var branches = expression.conditionalOr();
        if (!TryInfer(branches[0], symbols, out var condition))
        {
            return false;
        }

        if (branches.Length == 1)
        {
            type = condition;
            return true;
        }

        return condition.ValueType == AutomationPortValueType.Boolean
            && !condition.CanBeNull
            && TryInfer(branches[1], symbols, out var whenTrue)
            && TryInfer(expression.expr(), symbols, out var whenFalse)
            && TryMerge(whenTrue, whenFalse, out type);
    }

    private static bool TryInfer(
        CelParser.ConditionalOrContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    ) => TryLogical(expression.conditionalAnd(), symbols, out type);

    private static bool TryInfer(
        CelParser.ConditionalAndContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    ) => TryLogical(expression.relation(), symbols, out type);

    private static bool TryLogical<T>(
        IReadOnlyList<T> operands,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
        where T : IParseTree
    {
        type = default;
        if (operands.Count == 1)
        {
            return TryInferNode(operands[0], symbols, out type);
        }

        foreach (var operand in operands)
        {
            if (
                !TryInferNode(operand, symbols, out var operandType)
                || operandType.ValueType != AutomationPortValueType.Boolean
                || operandType.CanBeNull
            )
            {
                return false;
            }
        }

        type = Boolean();
        return true;
    }

    private static bool TryInfer(
        CelParser.RelationContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        var operands = expression.relation();
        if (operands.Length == 0)
        {
            return TryInfer(expression.calc(), symbols, out type);
        }

        if (
            operands.Length != 2
            || !TryInfer(operands[0], symbols, out var left)
            || !TryInfer(operands[1], symbols, out var right)
            || !Comparable(left, right, expression.op.Text)
        )
        {
            return false;
        }

        type = Boolean();
        return true;
    }

    private static bool TryInfer(
        CelParser.CalcContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        var operands = expression.calc();
        if (operands.Length == 0)
        {
            return TryInfer(expression.unary(), symbols, out type);
        }

        if (
            operands.Length != 2
            || !TryInfer(operands[0], symbols, out var left)
            || !TryInfer(operands[1], symbols, out var right)
            || left.CanBeNull
            || right.CanBeNull
        )
        {
            return false;
        }

        if (
            expression.op.Text == "+"
            && left.ValueType == AutomationPortValueType.Text
            && right.ValueType == AutomationPortValueType.Text
        )
        {
            type = Text();
            return true;
        }

        if (
            left.ValueType != AutomationPortValueType.Number
            || right.ValueType != AutomationPortValueType.Number
        )
        {
            return false;
        }

        type = NumberResult(left, right);
        return type.NumberKind != AutomationCelNumberKind.None;
    }

    private static bool TryInfer(
        CelParser.UnaryContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        switch (expression)
        {
            case CelParser.MemberExprContext member:
                return TryInfer(member.member(), symbols, out type);
            case CelParser.LogicalNotContext logical:
                if (
                    !TryInfer(logical.member(), symbols, out var logicalType)
                    || logicalType.ValueType != AutomationPortValueType.Boolean
                    || logicalType.CanBeNull
                )
                {
                    return false;
                }

                type = Boolean();
                return true;
            case CelParser.NegateContext negate:
                if (
                    !TryInfer(negate.member(), symbols, out var number)
                    || number.ValueType != AutomationPortValueType.Number
                    || number.CanBeNull
                    || number.NumberKind == AutomationCelNumberKind.UnsignedInteger
                )
                {
                    return false;
                }

                type = number with
                {
                    IntegerConstant = number.IntegerConstant is { } constant ? -constant : null,
                };
                return true;
            default:
                return false;
        }
    }

    private static bool TryInfer(
        CelParser.MemberContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        switch (expression)
        {
            case CelParser.PrimaryExprContext primary:
                return TryInfer(primary.primary(), symbols, out type);
            case CelParser.SelectContext select:
                return AutomationCelSyntax.TryPath(select, out var path)
                    && symbols.TryGetValue(path, out type);
            case CelParser.IndexContext index:
                if (
                    !TryInfer(index.member(), symbols, out var indexed)
                    || indexed.ValueType != AutomationPortValueType.Arguments
                    || indexed.CanBeNull
                    || !TryInfer(index.expr(), symbols, out var key)
                    || key.NumberKind != AutomationCelNumberKind.Integer
                    || key.CanBeNull
                )
                {
                    return false;
                }

                type = Text();
                return true;
            case CelParser.MemberCallContext call:
                return TryMemberCall(call, symbols, out type);
            default:
                return false;
        }
    }

    private static bool TryInfer(
        CelParser.PrimaryContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        switch (expression)
        {
            case CelParser.IdentOrGlobalCallContext identifier when identifier.LPAREN() is null:
                return symbols.TryGetValue(identifier.id.Text, out type);
            case CelParser.IdentOrGlobalCallContext call:
                return TryGlobalCall(call, symbols, out type);
            case CelParser.ConstantLiteralContext constant:
                return TryLiteral(constant.literal(), out type);
            case CelParser.NestedContext nested:
                return TryInfer(nested.expr(), symbols, out type);
            default:
                return false;
        }
    }

    private static bool TryGlobalCall(
        CelParser.IdentOrGlobalCallContext call,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        var arguments = call.exprList()?.expr() ?? [];
        var inferred = new AutomationCelStaticType[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!TryInfer(arguments[index], symbols, out inferred[index]))
            {
                return false;
            }
        }

        switch (call.id.Text)
        {
            case AutomationCelTransform.FunctionName
                when inferred.Length == 1
                    && inferred[0].NumberKind == AutomationCelNumberKind.Decimal
                    && !inferred[0].CanBeNull:
                type = Text();
                return true;
            case AutomationCelTransform.FunctionName
                when inferred.Length == 2
                    && inferred[0].NumberKind == AutomationCelNumberKind.Decimal
                    && !inferred[0].CanBeNull
                    && inferred[1].NumberKind == AutomationCelNumberKind.Integer
                    && (
                        inferred[1].IntegerConstant is null
                        || inferred[1].IntegerConstant is >= 0 and <= 6
                    ):
                type = Text();
                return true;
            case "timestamp" when OneNonNullable(inferred, AutomationPortValueType.Text):
                type = new(AutomationPortValueType.Timestamp, CanBeNull: false);
                return true;
            case "string" when inferred.Length == 1 && !inferred[0].CanBeNull:
                type = Text();
                return true;
            case "bool" when OneNonNullable(inferred, AutomationPortValueType.Boolean):
                type = Boolean();
                return true;
            case "decimal"
                when inferred.Length == 1
                    && inferred[0].ValueType == AutomationPortValueType.Number
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Decimal
                );
                return true;
            case "double"
                when inferred.Length == 1
                    && inferred[0].ValueType == AutomationPortValueType.Number
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Double
                );
                return true;
            case "int"
                when inferred.Length == 1
                    && inferred[0].ValueType == AutomationPortValueType.Number
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Integer
                );
                return true;
            case "uint"
                when inferred.Length == 1
                    && inferred[0].ValueType == AutomationPortValueType.Number
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.UnsignedInteger
                );
                return true;
            case "size"
                when inferred.Length == 1
                    && inferred[0].ValueType
                        is AutomationPortValueType.Text
                            or AutomationPortValueType.Arguments
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Integer
                );
                return true;
            default:
                return false;
        }
    }

    private static bool TryMemberCall(
        CelParser.MemberCallContext call,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        if (!TryInfer(call.member(), symbols, out var receiver) || receiver.CanBeNull)
        {
            return false;
        }

        var arguments = call.exprList()?.expr() ?? [];
        switch (call.id.Text)
        {
            case "size" when arguments.Length == 0:
                if (
                    receiver.ValueType
                    is not (AutomationPortValueType.Text or AutomationPortValueType.Arguments)
                )
                {
                    return false;
                }

                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Integer
                );
                return true;
            case "contains"
            or "endsWith"
            or "matches"
            or "startsWith"
                when receiver.ValueType == AutomationPortValueType.Text
                    && arguments.Length == 1
                    && TryInfer(arguments[0], symbols, out var argument)
                    && argument.ValueType == AutomationPortValueType.Text
                    && !argument.CanBeNull:
                type = Boolean();
                return true;
            default:
                return false;
        }
    }

    private static bool TryLiteral(
        CelParser.LiteralContext literal,
        out AutomationCelStaticType type
    )
    {
        type = literal switch
        {
            CelParser.StringContext => Text(),
            CelParser.BoolTrueContext or CelParser.BoolFalseContext => Boolean(),
            CelParser.IntContext integer
                when long.TryParse(
                    integer.GetText(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value
                ) => new(
                AutomationPortValueType.Number,
                CanBeNull: false,
                AutomationCelNumberKind.Integer,
                value
            ),
            CelParser.UintContext => new(
                AutomationPortValueType.Number,
                CanBeNull: false,
                AutomationCelNumberKind.UnsignedInteger
            ),
            CelParser.DoubleContext => new(
                AutomationPortValueType.Number,
                CanBeNull: false,
                AutomationCelNumberKind.Double
            ),
            CelParser.NullContext => new(null, CanBeNull: true),
            _ => default,
        };
        return literal
            is CelParser.StringContext
                or CelParser.BoolTrueContext
                or CelParser.BoolFalseContext
                or CelParser.IntContext
                or CelParser.UintContext
                or CelParser.DoubleContext
                or CelParser.NullContext;
    }

    private static bool TryInferNode(
        IParseTree node,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    ) =>
        node switch
        {
            CelParser.RelationContext relation => TryInfer(relation, symbols, out type),
            CelParser.ConditionalAndContext conditionalAnd => TryInfer(
                conditionalAnd,
                symbols,
                out type
            ),
            _ => Fail(out type),
        };

    private static bool TryMerge(
        AutomationCelStaticType left,
        AutomationCelStaticType right,
        out AutomationCelStaticType type
    )
    {
        type = default;
        if (left.ValueType is null && left.CanBeNull)
        {
            type = right with { CanBeNull = true, IntegerConstant = null };
            return right.ValueType is not null;
        }

        if (right.ValueType is null && right.CanBeNull)
        {
            type = left with { CanBeNull = true, IntegerConstant = null };
            return left.ValueType is not null;
        }

        if (left.ValueType != right.ValueType)
        {
            return false;
        }

        var numberKind =
            left.NumberKind == right.NumberKind ? left.NumberKind
            : left.ValueType == AutomationPortValueType.Number ? AutomationCelNumberKind.None
            : left.NumberKind;
        if (
            left.ValueType == AutomationPortValueType.Number
            && numberKind == AutomationCelNumberKind.None
        )
        {
            return false;
        }

        type = left with
        {
            CanBeNull = left.CanBeNull || right.CanBeNull,
            NumberKind = numberKind,
            IntegerConstant =
                left.IntegerConstant == right.IntegerConstant ? left.IntegerConstant : null,
        };
        return true;
    }

    private static AutomationCelStaticType NumberResult(
        AutomationCelStaticType left,
        AutomationCelStaticType right
    )
    {
        var kind =
            left.NumberKind == AutomationCelNumberKind.Double
            || right.NumberKind == AutomationCelNumberKind.Double
                ? AutomationCelNumberKind.Double
            : left.NumberKind == AutomationCelNumberKind.Decimal
            || right.NumberKind == AutomationCelNumberKind.Decimal
                ? AutomationCelNumberKind.Decimal
            : left.NumberKind == right.NumberKind ? left.NumberKind
            : AutomationCelNumberKind.None;
        return new(AutomationPortValueType.Number, CanBeNull: false, kind);
    }

    private static bool Comparable(
        AutomationCelStaticType left,
        AutomationCelStaticType right,
        string operation
    ) =>
        operation is "==" or "!="
            ? left.ValueType == right.ValueType
                || (left.ValueType is null && left.CanBeNull)
                || (right.ValueType is null && right.CanBeNull)
            : !left.CanBeNull
                && !right.CanBeNull
                && left.ValueType == right.ValueType
                && left.ValueType
                    is AutomationPortValueType.Text
                        or AutomationPortValueType.Number
                        or AutomationPortValueType.Timestamp;

    private static bool OneNonNullable(
        IReadOnlyList<AutomationCelStaticType> arguments,
        AutomationPortValueType type
    ) => arguments.Count == 1 && arguments[0].ValueType == type && !arguments[0].CanBeNull;

    private static bool Fail(out AutomationCelStaticType type)
    {
        type = default;
        return false;
    }
}

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

    internal static string RewriteIdentifier(string source, string from, string to)
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
                return source;
            }

            var rewritten = new StringBuilder();
            var offset = 0;
            foreach (var token in occurrences)
            {
                _ = rewritten.Append(source, offset, token.StartIndex - offset).Append(to);
                offset = token.StopIndex + 1;
            }

            return rewritten.Append(source[offset..]).ToString();
        }
        catch (CelException)
        {
            return source;
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
