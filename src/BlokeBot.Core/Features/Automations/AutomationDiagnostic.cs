using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.Automations;

public enum AutomationDiagnosticCategory
{
    Compilation,
    Evaluation,
    UnsupportedOperation,
    IncompatibleType,
    UnavailableName,
    InputUnavailable,
    ProducerFailed,
    HistoricalCauseUnavailable,
}

public readonly record struct AutomationExpressionSpan(int Start, int Length);

public abstract record AutomationDiagnosticMember
{
    private AutomationDiagnosticMember() { }

    public abstract T Match<T>(Func<Input, T> input, Func<Output, T> output, Func<Node, T> node);

    public sealed record Input(AutomationConfigurationFieldId FieldId, AutomationPortId PortId)
        : AutomationDiagnosticMember
    {
        public override T Match<T>(
            Func<Input, T> input,
            Func<Output, T> output,
            Func<Node, T> node
        ) => input(this);
    }

    public sealed record Output(AutomationPortId PortId) : AutomationDiagnosticMember
    {
        public override T Match<T>(
            Func<Input, T> input,
            Func<Output, T> output,
            Func<Node, T> node
        ) => output(this);
    }

    public sealed record Node : AutomationDiagnosticMember
    {
        public override T Match<T>(
            Func<Input, T> input,
            Func<Output, T> output,
            Func<Node, T> node
        ) => node(this);
    }
}

public sealed record AutomationDiagnosticSite(
    AutomationNodeId NodeId,
    AutomationDefinitionId DefinitionId,
    AutomationDiagnosticMember Member,
    AutomationTraceInvocation? Invocation
);

[JsonConverter(typeof(AutomationDiagnosticJsonConverter))]
public sealed record AutomationDiagnostic(
    AutomationDiagnosticCategory Category,
    AutomationDiagnosticMember Member,
    AutomationExpressionSpan? Span = null,
    AutomationDiagnosticSite? Origin = null,
    AutomationDiagnosticSite? Affected = null
)
{
    internal static AutomationDiagnostic Expression(
        AutomationDiagnosticCategory category,
        string? source
    ) => new(category, new AutomationDiagnosticMember.Node(), new(0, source?.Length ?? 0));

    internal AutomationDiagnostic At(
        AutomationRuntimeSerialization.PersistedNode node,
        AutomationDiagnosticMember member
    )
    {
        var site = new AutomationDiagnosticSite(
            new(node.AuthorNodeId ?? node.Id),
            new(node.DefinitionId),
            member,
            node.Invocation
        );
        return Origin is null
            ? this with
            {
                Origin = site,
                Member = member,
            }
            : this with
            {
                Affected = site,
            };
    }

    internal AutomationDiagnostic Offset(int offset) =>
        Span is { } span ? this with { Span = span with { Start = span.Start + offset } } : this;

    public string Summary =>
        Category switch
        {
            AutomationDiagnosticCategory.Compilation => "Expression could not be compiled",
            AutomationDiagnosticCategory.Evaluation => "Expression could not be evaluated",
            AutomationDiagnosticCategory.UnsupportedOperation => "Operation is not supported here",
            AutomationDiagnosticCategory.IncompatibleType => "Expression has an incompatible type",
            AutomationDiagnosticCategory.UnavailableName => "Name is unavailable in this scope",
            AutomationDiagnosticCategory.InputUnavailable => "Input is unavailable",
            AutomationDiagnosticCategory.ProducerFailed => "Producer failed",
            AutomationDiagnosticCategory.HistoricalCauseUnavailable => "Producer previously failed",
        };

    public string Correction =>
        Category switch
        {
            AutomationDiagnosticCategory.UnsupportedOperation =>
                "Use a supported function and conversion for this scope.",
            AutomationDiagnosticCategory.IncompatibleType =>
                "Match the declared type and whether a value may be null.",
            AutomationDiagnosticCategory.UnavailableName =>
                "Use only the inputs available to this expression.",
            AutomationDiagnosticCategory.InputUnavailable =>
                "Check this input's binding and connected output.",
            AutomationDiagnosticCategory.ProducerFailed =>
                "Check the connected producer before this consumer.",
            AutomationDiagnosticCategory.HistoricalCauseUnavailable =>
                "The original cause is unavailable here. Check the producer's retained trace; it has not been run again.",
            AutomationDiagnosticCategory.Compilation or AutomationDiagnosticCategory.Evaluation =>
                "Check this expression and its supplied inputs.",
        };
}
