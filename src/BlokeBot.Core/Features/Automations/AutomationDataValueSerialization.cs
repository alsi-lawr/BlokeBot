using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationOutputRestoreOutcome
{
    private AutomationOutputRestoreOutcome() { }

    internal sealed record Available(
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> Outputs
    ) : AutomationOutputRestoreOutcome;

    internal sealed record Invalid : AutomationOutputRestoreOutcome;
}

internal static class AutomationDataValueSerialization
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    internal static string SerializeOutputs(
        IReadOnlyDictionary<AutomationPortId, AutomationResolvedValue> outputs
    ) =>
        JsonSerializer.Serialize(
            outputs
                .OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal)
                .Select(static pair => new PersistedOutput(pair.Key.Value, Persist(pair.Value)))
                .ToImmutableArray(),
            _options
        );

    internal static AutomationOutputRestoreOutcome RestoreOutputs(string json)
    {
        ImmutableArray<PersistedOutput> persisted;
        try
        {
            persisted = JsonSerializer.Deserialize<ImmutableArray<PersistedOutput>>(json, _options);
        }
        catch (JsonException)
        {
            return new AutomationOutputRestoreOutcome.Invalid();
        }
        catch (NotSupportedException)
        {
            return new AutomationOutputRestoreOutcome.Invalid();
        }

        if (persisted.IsDefault)
        {
            return new AutomationOutputRestoreOutcome.Invalid();
        }

        var outputs = ImmutableDictionary.CreateBuilder<
            AutomationPortId,
            AutomationResolvedValue
        >();
        foreach (var output in persisted)
        {
            if (
                output is null
                || string.IsNullOrWhiteSpace(output.PortId)
                || !TryRestore(output.Value, out var value)
                || !outputs.TryAdd(new(output.PortId), value)
            )
            {
                return new AutomationOutputRestoreOutcome.Invalid();
            }
        }

        return new AutomationOutputRestoreOutcome.Available(outputs.ToImmutable());
    }

    internal static ImmutableArray<AutomationValueDiagnostic> Diagnostics(
        IReadOnlyDictionary<AutomationPortId, AutomationResolvedValue> outputs
    ) =>
        [
            .. outputs
                .OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal)
                .Select(static pair => new AutomationValueDiagnostic(
                    pair.Key,
                    AutomationPureHandlerRegistry.ValueType(pair.Value.Value),
                    pair.Value.Provenance,
                    pair.Value.ValueFreeDiagnostic ? "available" : Display(pair.Value.Value),
                    pair.Value.SafeTriggerFields
                )),
        ];

    private static PersistedValue Persist(AutomationResolvedValue resolved) =>
        resolved.Value switch
        {
            AutomationValue.Text text => Value(
                "text",
                text.Value,
                resolved.Provenance,
                resolved.SafeTriggerFields,
                resolved.ValueFreeDiagnostic
            ),
            AutomationValue.Number number => Value(
                "number",
                number.Value,
                resolved.Provenance,
                resolved.SafeTriggerFields,
                resolved.ValueFreeDiagnostic
            ),
            AutomationValue.Boolean boolean => Value(
                "boolean",
                boolean.Value,
                resolved.Provenance,
                resolved.SafeTriggerFields,
                resolved.ValueFreeDiagnostic
            ),
            AutomationValue.Timestamp timestamp => Value(
                "timestamp",
                timestamp.Value,
                resolved.Provenance,
                resolved.SafeTriggerFields,
                resolved.ValueFreeDiagnostic
            ),
            AutomationValue.Actor actor => Value(
                "actor",
                actor.Value,
                resolved.Provenance,
                resolved.SafeTriggerFields,
                resolved.ValueFreeDiagnostic
            ),
            AutomationValue.Channel channel => Value(
                "channel",
                channel.Value,
                resolved.Provenance,
                resolved.SafeTriggerFields,
                resolved.ValueFreeDiagnostic
            ),
            AutomationValue.Stream stream => Value(
                "stream",
                stream.Value,
                resolved.Provenance,
                resolved.SafeTriggerFields,
                resolved.ValueFreeDiagnostic
            ),
            AutomationValue.Arguments arguments => Value(
                "arguments",
                arguments
                    .Values.Select(static argument => new PersistedArgument(
                        argument.Position,
                        argument.Value,
                        Names(argument.Provenance)
                    ))
                    .ToImmutableArray(),
                resolved.Provenance,
                resolved.SafeTriggerFields,
                resolved.ValueFreeDiagnostic
            ),
            AutomationValue.Null nullValue
                when nullValue.ValueType != AutomationPortValueType.Flow => Value(
                "null",
                nullValue.ValueType.ToString(),
                resolved.Provenance,
                resolved.SafeTriggerFields,
                resolved.ValueFreeDiagnostic
            ),
            _ => throw new InvalidOperationException("Unknown automation output value."),
        };

    private static PersistedValue Value<T>(
        string kind,
        T value,
        ImmutableArray<AutomationValueProvenance> provenance,
        ImmutableArray<AutomationSafeTriggerFieldId> safeTriggerFields,
        bool valueFreeDiagnostic
    ) =>
        new(
            kind,
            JsonSerializer.Serialize(value, _options),
            Names(provenance),
            safeTriggerFields.IsDefaultOrEmpty
                ? []
                :
                [
                    .. safeTriggerFields
                        .OrderBy(static field => field.Value, StringComparer.Ordinal)
                        .Select(static field => field.Value),
                ],
            valueFreeDiagnostic
        );

    private static bool TryRestore(PersistedValue? persisted, out AutomationResolvedValue value)
    {
        value = null!;
        if (
            persisted is null
            || string.IsNullOrWhiteSpace(persisted.Kind)
            || persisted.ValueJson is null
            || !TryRestoreProvenance(persisted.Provenance, out var provenance)
            || !TryRestoreSafeTriggerFields(persisted.SafeTriggerFields, out var safeTriggerFields)
        )
        {
            return false;
        }

        try
        {
            AutomationValue? restored = persisted.Kind switch
            {
                "text" => JsonSerializer.Deserialize<string>(persisted.ValueJson, _options)
                    is { } text
                    ? new AutomationValue.Text(text)
                    : null,
                "number" => new AutomationValue.Number(
                    JsonSerializer.Deserialize<decimal>(persisted.ValueJson, _options)
                ),
                "boolean" => new AutomationValue.Boolean(
                    JsonSerializer.Deserialize<bool>(persisted.ValueJson, _options)
                ),
                "timestamp" => new AutomationValue.Timestamp(
                    JsonSerializer.Deserialize<DateTimeOffset>(persisted.ValueJson, _options)
                ),
                "actor" => JsonSerializer.Deserialize<AutomationPublicActor>(
                    persisted.ValueJson,
                    _options
                )
                    is { } actor
                    ? new AutomationValue.Actor(actor)
                    : null,
                "channel" => JsonSerializer.Deserialize<AutomationPublicChannel>(
                    persisted.ValueJson,
                    _options
                )
                    is { } channel
                    ? new AutomationValue.Channel(channel)
                    : null,
                "stream" => JsonSerializer.Deserialize<AutomationPublicStream>(
                    persisted.ValueJson,
                    _options
                )
                    is { } stream
                    ? new AutomationValue.Stream(stream)
                    : null,
                "arguments" => RestoreArguments(persisted.ValueJson),
                "null" => RestoreNull(persisted.ValueJson),
                _ => null,
            };
            if (restored is null)
            {
                return false;
            }

            value = new(restored, provenance, safeTriggerFields, persisted.ValueFreeDiagnostic);
            return ValidRestoredValue(value);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static AutomationValue? RestoreArguments(string json)
    {
        var persisted = JsonSerializer.Deserialize<ImmutableArray<PersistedArgument>>(
            json,
            _options
        );
        if (persisted.IsDefault)
        {
            return null;
        }

        var arguments = ImmutableArray.CreateBuilder<AutomationValueArgument>();
        foreach (var argument in persisted)
        {
            if (
                argument is null
                || argument.Value is null
                || !TryRestoreProvenance(argument.Provenance, out var provenance)
            )
            {
                return null;
            }

            arguments.Add(new(argument.Position, argument.Value, provenance));
        }

        return new AutomationValue.Arguments(arguments.ToImmutable());
    }

    private static AutomationValue? RestoreNull(string json)
    {
        var name = JsonSerializer.Deserialize<string>(json, _options);
        return
            Enum.TryParse<AutomationPortValueType>(name, out var valueType)
            && Enum.IsDefined(valueType)
            && valueType != AutomationPortValueType.Flow
            && name == valueType.ToString()
            ? new AutomationValue.Null(valueType)
            : null;
    }

    private static bool TryRestoreProvenance(
        ImmutableArray<string> names,
        out ImmutableArray<AutomationValueProvenance> provenance
    )
    {
        provenance = [];
        if (names.IsDefaultOrEmpty)
        {
            return false;
        }

        var restored = ImmutableArray.CreateBuilder<AutomationValueProvenance>();
        foreach (var name in names)
        {
            if (
                !Enum.TryParse<AutomationValueProvenance>(name, out var value)
                || !Enum.IsDefined(value)
                || name != value.ToString()
            )
            {
                return false;
            }

            restored.Add(value);
        }

        provenance = [.. restored.Distinct().Order()];
        return provenance.Length == names.Length;
    }

    private static bool TryRestoreSafeTriggerFields(
        ImmutableArray<string> names,
        out ImmutableArray<AutomationSafeTriggerFieldId> fields
    )
    {
        fields = [];
        if (names.IsDefaultOrEmpty)
        {
            return true;
        }

        if (
            names.Any(static name => string.IsNullOrWhiteSpace(name) || name.Length > 96)
            || names.Distinct(StringComparer.Ordinal).Count() != names.Length
        )
        {
            return false;
        }

        fields =
        [
            .. names
                .Order(StringComparer.Ordinal)
                .Select(static name => new AutomationSafeTriggerFieldId(name)),
        ];
        return true;
    }

    private static bool ValidRestoredValue(AutomationResolvedValue resolved)
    {
        if (resolved.Value is AutomationValue.Actor actor)
        {
            string? login = actor.Value.Login;
            string? displayName = actor.Value.DisplayName;
            if (login is null || displayName is null)
            {
                return false;
            }
        }

        if (resolved.Value is AutomationValue.Channel channel)
        {
            string? login = channel.Value.Login;
            string? displayName = channel.Value.DisplayName;
            if (login is null || displayName is null)
            {
                return false;
            }
        }

        if (resolved.Value is not AutomationValue.Arguments arguments)
        {
            return true;
        }

        if (arguments.Values.IsEmpty)
        {
            return true;
        }

        var positions = arguments.Values.Select(static argument => argument.Position).ToArray();
        var nested = arguments
            .Values.SelectMany(static argument => argument.Provenance)
            .ToImmutableHashSet();
        return positions.All(static position => position >= 0)
            && positions.SequenceEqual(positions.Order().Distinct())
            && nested.SetEquals(resolved.Provenance);
    }

    private static ImmutableArray<string> Names(
        ImmutableArray<AutomationValueProvenance> provenance
    ) => [.. provenance.Select(static value => value.ToString())];

    private static string Display(AutomationValue value)
    {
        var rendered = value switch
        {
            AutomationValue.Text text => text.Value,
            AutomationValue.Number number => number.Value.ToString(CultureInfo.InvariantCulture),
            AutomationValue.Boolean boolean => boolean.Value ? "true" : "false",
            AutomationValue.Timestamp timestamp => timestamp.Value.ToString(
                "O",
                CultureInfo.InvariantCulture
            ),
            AutomationValue.Actor actor => $"{actor.Value.DisplayName} ({actor.Value.Login})",
            AutomationValue.Channel channel =>
                $"{channel.Value.DisplayName} ({channel.Value.Login})",
            AutomationValue.Stream stream => stream.Value.Title
                ?? stream.Value.GameName
                ?? "stream",
            AutomationValue.Arguments arguments => string.Join(
                " ",
                arguments.Values.Select(static argument => argument.Value)
            ),
            AutomationValue.Null => "null",
            _ => string.Empty,
        };
        return rendered.Length <= 200 ? rendered : string.Concat(rendered.AsSpan(0, 200), "...");
    }

    private sealed record PersistedOutput(string PortId, PersistedValue Value);

    private sealed record PersistedValue(
        string Kind,
        string ValueJson,
        ImmutableArray<string> Provenance,
        ImmutableArray<string> SafeTriggerFields,
        bool ValueFreeDiagnostic
    );

    private sealed record PersistedArgument(
        int Position,
        string Value,
        ImmutableArray<string> Provenance
    );
}
