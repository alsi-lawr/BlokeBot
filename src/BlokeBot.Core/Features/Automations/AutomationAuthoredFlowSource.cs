using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.Automations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationAuthoredFlowSource(
    [property: JsonRequired] string Name,
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] AutomationFlowCanvasSettings Canvas,
    [property: JsonRequired] ImmutableArray<AutomationAuthoredFlowNode> Nodes,
    [property: JsonRequired] ImmutableArray<AutomationAuthoredFlowEdge> Edges
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationAuthoredFlowNode(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string DefinitionId,
    [property: JsonRequired] int DefinitionSchemaVersion,
    [property: JsonRequired] string ConfigurationJson,
    [property: JsonRequired] string InputBindingsJson,
    [property: JsonRequired] int ExpressionLanguageVersion,
    [property: JsonRequired] AutomationNodeFailurePolicy FailurePolicy,
    [property: JsonRequired] AutomationAuthoredFlowPosition Position,
    [property: JsonRequired] string? DisplayAlias
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationAuthoredFlowPosition(
    [property: JsonRequired] int X,
    [property: JsonRequired] int Y
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationAuthoredFlowEdge(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] AutomationEdgeKind Kind,
    [property: JsonRequired] Guid SourceNodeId,
    [property: JsonRequired] string SourcePortId,
    [property: JsonRequired] Guid TargetNodeId,
    [property: JsonRequired] string TargetPortId
);

internal sealed record AutomationFlowSourceError(string Path, string Message);

internal abstract record AutomationFlowSourceParseOutcome
{
    private AutomationFlowSourceParseOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Parsed, TResult> parsed,
        Func<Invalid, TResult> invalid
    );

    internal sealed record Parsed(AutomationAuthoredFlowSource Source)
        : AutomationFlowSourceParseOutcome
    {
        internal override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<Invalid, TResult> invalid
        ) => parsed(this);
    }

    internal sealed record Invalid(ImmutableArray<AutomationFlowSourceError> Errors)
        : AutomationFlowSourceParseOutcome
    {
        internal override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<Invalid, TResult> invalid
        ) => invalid(this);
    }
}

internal static class AutomationAuthoredFlowCodec
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private static readonly JsonSerializerOptions _writeOptions = WriteOptions();

    private static JsonSerializerOptions WriteOptions()
    {
        var options = new JsonSerializerOptions(_options);
        options.Converters.Clear();
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: true));
        return options;
    }

    internal static string Write(AutomationAuthoredFlowSource source) =>
        JsonSerializer.Serialize(source, _writeOptions);

    internal static AutomationFlowSourceParseOutcome Parse(string text)
    {
        AutomationAuthoredFlowSource? source;
        JsonElement json;
        try
        {
            using var document = JsonDocument.Parse(text);
            json = document.RootElement.Clone();
            if (DuplicateMember(json, "$") is { } duplicate)
            {
                return Invalid(duplicate, "Remove this duplicate source member.");
            }
            source = json.Deserialize<AutomationAuthoredFlowSource>(_options);
        }
        catch (JsonException exception)
        {
            return Invalid(
                exception.Path ?? "$",
                $"{exception.Message} (line {exception.LineNumber}, column {exception.BytePositionInLine})."
            );
        }
        if (source is null || source.Nodes.IsDefault || source.Edges.IsDefault)
        {
            return Invalid("$", "Supply the authored flow, nodes and edges.");
        }
        if (
            !CanonicalEnum(json.GetProperty("canvas"), "orientation", source.Canvas.Orientation)
            || !CanonicalEnum(json.GetProperty("canvas"), "edgeStyle", source.Canvas.EdgeStyle)
        )
        {
            return Invalid("$.canvas", "Choose a supported orientation and edge style.");
        }
        for (var index = 0; index < source.Nodes.Length; index++)
        {
            var node = source.Nodes[index];
            var path = $"$.nodes[{index}]";
            if (
                node is null
                || node.Position is null
                || node.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(node.DefinitionId)
                || node.DefinitionSchemaVersion <= 0
                || node.ExpressionLanguageVersion <= 0
            )
            {
                return Invalid(
                    path,
                    "Supply a valid node identity, definition, versions and position."
                );
            }
            if (
                !CanonicalEnum(
                    json.GetProperty("nodes")[index],
                    "failurePolicy",
                    node.FailurePolicy
                )
            )
            {
                return Invalid(path + ".failurePolicy", "Choose Stop or Continue.");
            }
            if (
                ReadConfiguration(node.ConfigurationJson, path + ".configurationJson") is
                { } configurationError
            )
            {
                return new AutomationFlowSourceParseOutcome.Invalid([configurationError]);
            }
            if (
                ReadBindings(
                    node.InputBindingsJson,
                    new(node.DefinitionId),
                    path + ".inputBindingsJson"
                ) is
                { } bindingError
            )
            {
                return new AutomationFlowSourceParseOutcome.Invalid([bindingError]);
            }
        }
        for (var index = 0; index < source.Edges.Length; index++)
        {
            var edge = source.Edges[index];
            if (
                edge is null
                || edge.Id == Guid.Empty
                || edge.SourceNodeId == Guid.Empty
                || edge.TargetNodeId == Guid.Empty
                || string.IsNullOrWhiteSpace(edge.SourcePortId)
                || string.IsNullOrWhiteSpace(edge.TargetPortId)
                || !CanonicalEnum(json.GetProperty("edges")[index], "kind", edge.Kind)
            )
            {
                return Invalid(
                    $"$.edges[{index}]",
                    "Supply a valid edge identity, kind and endpoints."
                );
            }
        }
        return new AutomationFlowSourceParseOutcome.Parsed(source);
    }

    internal static AutomationFlowSourceError? ReadConfiguration(string text, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind != JsonValueKind.Object
                    ? new(path, "Use a JSON configuration object.")
                : DuplicateMember(document.RootElement, path) is { } duplicate
                    ? new(duplicate, "Remove this duplicate configuration member.")
                : null;
        }
        catch (JsonException exception)
        {
            return new(
                path,
                $"{exception.Message} (line {exception.LineNumber}, column {exception.BytePositionInLine})."
            );
        }
    }

    internal static AutomationFlowSourceError? ReadBindings(
        string text,
        AutomationDefinitionId definitionId,
        string path,
        bool forAuthoring = false
    )
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new(path, "Use a JSON input-binding object.");
            }
            if (DuplicateMember(document.RootElement, path) is { } duplicate)
            {
                return new(duplicate, "Remove this duplicate binding member.");
            }
            foreach (var field in document.RootElement.EnumerateObject())
            {
                var fieldPath = path + "." + field.Name;
                if (
                    field.Value.ValueKind != JsonValueKind.Object
                    || field
                        .Value.EnumerateObject()
                        .Any(member => member.Name is not ("mode" or "expression"))
                    || !field.Value.TryGetProperty("mode", out var mode)
                    || mode.ValueKind != JsonValueKind.String
                )
                {
                    return new(fieldPath, "Use this input's mode and optional expression only.");
                }
                if (
                    field.Value.TryGetProperty("expression", out var expression)
                    && expression.ValueKind != JsonValueKind.Null
                    && (
                        expression.ValueKind != JsonValueKind.Object
                        || expression
                            .EnumerateObject()
                            .Any(member => member.Name is not ("languageVersion" or "source"))
                        || !expression.TryGetProperty("languageVersion", out var version)
                        || version.ValueKind != JsonValueKind.Number
                        || !version.TryGetInt32(out var value)
                        || value <= 0
                        || !expression.TryGetProperty("source", out var source)
                        || source.ValueKind != JsonValueKind.String
                    )
                )
                {
                    return new(
                        fieldPath + ".expression",
                        "Supply a string expression and positive language version."
                    );
                }
            }
            return
                (
                    forAuthoring
                        ? AutomationRuntimeSerialization.RestoreAuthoringInputBindings(
                            text,
                            definitionId
                        )
                        : AutomationRuntimeSerialization.RestoreInputBindings(text, definitionId)
                ) is AutomationInputBindingsRestoreOutcome.Available
                ? null
                : new(path, "Repair the input modes and active expression values.");
        }
        catch (JsonException exception)
        {
            return new(
                path,
                $"{exception.Message} (line {exception.LineNumber}, column {exception.BytePositionInLine})."
            );
        }
    }

    private static bool CanonicalEnum<T>(JsonElement json, string property, T value)
        where T : struct, Enum =>
        Enum.IsDefined(value)
        && json.TryGetProperty(property, out var member)
        && member.ValueKind == JsonValueKind.String
        && member.GetString() == value.ToString();

    private static string? DuplicateMember(JsonElement json, string path)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in json.EnumerateObject())
            {
                var childPath = path + "." + member.Name;
                if (!names.Add(member.Name))
                {
                    return childPath;
                }

                if (DuplicateMember(member.Value, childPath) is { } duplicate)
                {
                    return duplicate;
                }
            }
        }
        else if (json.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in json.EnumerateArray())
            {
                if (DuplicateMember(item, $"{path}[{index++}]") is { } duplicate)
                {
                    return duplicate;
                }
            }
        }
        return null;
    }

    private static AutomationFlowSourceParseOutcome Invalid(string path, string message) =>
        new AutomationFlowSourceParseOutcome.Invalid([new(path, message)]);
}
