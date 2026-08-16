using System.Collections.Immutable;
using Cel;

namespace BlokeBot.Core.Features.Automations;

public static class AutomationSafeTriggerView
{
    public const int CurrentVersion = 1;
}

public enum AutomationSafeTriggerFieldStatus
{
    Available,
    ReservedName,
    Collision,
    Incompatible,
}

public sealed record AutomationSafeTriggerViewField(
    AutomationSafeTriggerFieldId Id,
    string Path,
    AutomationPortValueType ValueType,
    AutomationPortNullability Nullability,
    AutomationValueProvenance Provenance,
    AutomationSafeTriggerFieldStatus Status
);

public sealed record AutomationSafeTriggerViewDescriptor(
    int Version,
    ImmutableArray<AutomationSafeTriggerViewField> Fields
)
{
    public ImmutableArray<AutomationSafeTriggerViewField> AvailableFields =>
        [
            .. Fields.Where(static candidate =>
                candidate.Status == AutomationSafeTriggerFieldStatus.Available
            ),
        ];
}

internal static class AutomationSafeTriggerViewResolver
{
    private static readonly ImmutableHashSet<string> _reservedRoots = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "event",
        "timestamps",
        AutomationCelTransform.FunctionName
    );

    internal static bool TryBuild(
        AutomationCatalogService catalog,
        AutomationRuntimeSerialization.PersistedFlow flow,
        AutomationRuntimeSerialization.PersistedNode transform,
        out AutomationSafeTriggerViewDescriptor descriptor
    )
    {
        descriptor = null!;
        var definitions = new Dictionary<Guid, AutomationConfigurationCheck.Valid>();
        foreach (var node in flow.Nodes)
        {
            if (
                catalog.ValidatePersistedDefinition(AutomationRuntimeSerialization.Definition(node))
                is not AutomationConfigurationCheck.Valid valid
            )
            {
                return false;
            }

            if (!definitions.TryAdd(node.Id, valid))
            {
                return false;
            }
        }

        if (
            !definitions.TryGetValue(transform.Id, out var transformDefinition)
            || transformDefinition.Definition.Kind != AutomationNodeKind.Transform
        )
        {
            return false;
        }

        var flowAdjacency = definitions.Keys.ToDictionary(
            static nodeId => nodeId,
            static _ => new List<Guid>()
        );
        var dataAdjacency = definitions.Keys.ToDictionary(
            static nodeId => nodeId,
            static _ => new List<Guid>()
        );
        foreach (var edge in flow.Edges)
        {
            var adjacency = edge.Kind == AutomationEdgeKind.Flow ? flowAdjacency : dataAdjacency;
            if (
                !adjacency.TryGetValue(edge.SourceNodeId, out var targets)
                || !adjacency.ContainsKey(edge.TargetNodeId)
            )
            {
                return false;
            }

            targets.Add(edge.TargetNodeId);
        }

        var consumers = Reachable([transform.Id], dataAdjacency)
            .Where(nodeId =>
                definitions[nodeId].Definition.Kind
                    is AutomationNodeKind.Action
                        or AutomationNodeKind.Control
            )
            .ToHashSet();
        var sources = definitions
            .Where(static pair => pair.Value.Definition.Kind == AutomationNodeKind.Source)
            .Where(pair =>
                consumers.Count == 0 || Reachable([pair.Key], flowAdjacency).Overlaps(consumers)
            )
            .Select(static pair => pair.Value.SafeTriggerSource)
            .ToArray();
        descriptor = Build(sources);
        return true;
    }

    internal static AutomationSafeTriggerViewDescriptor Build(
        IReadOnlyCollection<AutomationSafeTriggerSourceContract?> sources
    )
    {
        var all = sources
            .SelectMany(static source => source?.Fields ?? [])
            .GroupBy(static field => field.Path, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal);
        var paths = all.Select(static group => group.Key).ToArray();
        var fields = ImmutableArray.CreateBuilder<AutomationSafeTriggerViewField>();
        foreach (var group in all)
        {
            var contracts = group.ToArray();
            var root = Root(group.Key);
            var status =
                _reservedRoots.Contains(root)
                || (
                    !group.Key.Contains('.')
                    && _rootNames.Contains(group.Key)
                    && group.Key != "arguments"
                )
                    ? AutomationSafeTriggerFieldStatus.ReservedName
                : contracts.Select(static field => field.Id).Distinct().Count() != 1
                || paths.Any(path =>
                    path != group.Key
                    && (
                        path.StartsWith($"{group.Key}.", StringComparison.Ordinal)
                        || group.Key.StartsWith($"{path}.", StringComparison.Ordinal)
                    )
                )
                    ? AutomationSafeTriggerFieldStatus.Collision
                : contracts
                    .Select(static field => (field.ValueType, field.Nullability, field.Provenance))
                    .Distinct()
                    .Count() != 1
                    ? AutomationSafeTriggerFieldStatus.Incompatible
                : contracts.Length != sources.Count ? AutomationSafeTriggerFieldStatus.Incompatible
                : AutomationSafeTriggerFieldStatus.Available;
            var first = contracts[0];
            fields.Add(
                new(
                    first.Id,
                    first.Path,
                    first.ValueType,
                    first.Nullability,
                    first.Provenance,
                    status
                )
            );
        }

        return new(AutomationSafeTriggerView.CurrentVersion, fields.ToImmutable());
    }

    private static ImmutableHashSet<string> _rootNames { get; } =
        ImmutableHashSet.Create(StringComparer.Ordinal, "actor", "channel", "stream", "arguments");

    private static string Root(string path)
    {
        var separator = path.IndexOf('.');
        return separator < 0 ? path : path[..separator];
    }

    private static HashSet<Guid> Reachable(
        IEnumerable<Guid> starts,
        IReadOnlyDictionary<Guid, List<Guid>> adjacency
    )
    {
        var reached = new HashSet<Guid>();
        var pending = new Stack<Guid>(starts);
        while (pending.TryPop(out var nodeId) && reached.Add(nodeId))
        {
            if (!adjacency.TryGetValue(nodeId, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                pending.Push(target);
            }
        }

        return reached;
    }
}

internal sealed class AutomationSafeTriggerExpressionService
{
    private readonly CelEnvironment _environment =
        AutomationTransformCelService.CreateEnvironment();

    internal bool Validate(
        AutomationExpressionSource expression,
        AutomationPortMetadata target,
        AutomationSafeTriggerViewDescriptor descriptor,
        out ImmutableArray<AutomationSafeTriggerViewField> references
    ) => Validate(expression, target, descriptor, out references, out _);

    internal bool Validate(
        AutomationExpressionSource expression,
        AutomationPortMetadata target,
        AutomationSafeTriggerViewDescriptor descriptor,
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

        var available = descriptor.AvailableFields.ToDictionary(
            static field => field.Path,
            StringComparer.Ordinal
        );
        var resolved = ImmutableArray.CreateBuilder<AutomationSafeTriggerViewField>();
        foreach (var reference in analysis.References.Order(StringComparer.Ordinal))
        {
            if (
                !available.TryGetValue(reference, out var field)
                && !TryResolveProjectedMember(reference, available, out field)
            )
            {
                invalidField = descriptor
                    .Fields.FirstOrDefault(candidate => candidate.Path == reference)
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
                AutomationCelStaticTypes.ForSafeView(descriptor),
                out var result
            ) || !result.IsAssignableTo(target.ValueType, target.Nullability)
        )
        {
            return false;
        }

        references = resolved.ToImmutable();
        return true;
    }

    private static bool TryResolveProjectedMember(
        string reference,
        IReadOnlyDictionary<string, AutomationSafeTriggerViewField> available,
        out AutomationSafeTriggerViewField field
    )
    {
        field = null!;
        var separator = reference.IndexOf('.');
        if (
            separator <= 0
            || !available.TryGetValue(reference[..separator], out var candidate)
            || !AutomationCelSyntax.AllowedField(candidate.ValueType, reference[(separator + 1)..])
        )
        {
            return false;
        }

        field = candidate;
        return true;
    }

    internal AutomationResolvedValue? Evaluate(
        AutomationExpressionSource expression,
        AutomationPortMetadata port,
        AutomationSafeTriggerViewDescriptor descriptor,
        AutomationContext context
    )
    {
        if (!Validate(expression, port, descriptor, out var references))
        {
            return null;
        }

        Dictionary<string, object?> bindings = new(StringComparer.Ordinal);
        foreach (var field in references)
        {
            if (!TryBind(bindings, field, context))
            {
                return null;
            }
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

    private static bool TryBind(
        Dictionary<string, object?> bindings,
        AutomationSafeTriggerViewField field,
        AutomationContext context
    )
    {
        object? value = field.Path switch
        {
            "actor.login" => context.Actor?.Login,
            "actor.display_name" => context.Actor?.DisplayName,
            "channel.login" => context.Channel.Login,
            "channel.display_name" => context.Channel.DisplayName,
            "stream.title" => context.Stream?.Title,
            "stream.game_name" => context.Stream?.GameName,
            "stream.started_at" => context.Stream?.StartedAtUtc,
            "arguments" => context
                .Arguments.OrderBy(static argument => argument.Position)
                .Select(static argument => argument.Value)
                .ToArray(),
            _ => ProjectedValue(field, context),
        };
        if (
            ReferenceEquals(value, Missing.Value)
            || (value is null && field.Nullability != AutomationPortNullability.Nullable)
        )
        {
            return false;
        }

        var segments = field.Path.Split('.');
        if (segments.Length == 1)
        {
            bindings[field.Path] = value;
            return true;
        }

        IDictionary<string, object?> current = bindings;
        foreach (var segment in segments[..^1])
        {
            if (!current.TryGetValue(segment, out var existing))
            {
                existing = new Dictionary<string, object?>(StringComparer.Ordinal);
                current.Add(segment, existing);
            }

            if (existing is not IDictionary<string, object?> nested)
            {
                return false;
            }

            current = nested;
        }

        current[segments[^1]] = value;
        return true;
    }

    private static object? ProjectedValue(
        AutomationSafeTriggerViewField field,
        AutomationContext context
    ) =>
        !context.Variables.ForExecution().TryGetValue(new(field.Path), out var variable)
            ? field.Nullability == AutomationPortNullability.Nullable
                ? null
                : Missing.Value
            : variable.Sensitivity == AutomationDataSensitivity.Safe
            && AutomationPureHandlerRegistry.ValueType(variable.Value) == field.ValueType
                ? AutomationTransformCelService.ToCelValue(variable.Value)
                : Missing.Value;

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
                                [AutomationValueProvenance.PublicChat]
                            )
                    ),
                ]),
            _ => null!,
        };
        return value is not null;
    }

    private sealed class Missing
    {
        internal static object Value { get; } = new();
    }
}
