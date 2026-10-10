using System.Diagnostics;
using System.Text.Json.Serialization;
using BlokeBot.Core.Features.Automations;

namespace BlokeBot.Core.Features.Dashboards;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DashboardTarget.Automation), "automation")]
[JsonDerivedType(typeof(DashboardTarget.Value), "value")]
[JsonDerivedType(typeof(DashboardTarget.Countdown), "countdown")]
[JsonDerivedType(typeof(DashboardTarget.Poll), "poll")]
[JsonDerivedType(typeof(DashboardTarget.Prediction), "prediction")]
[JsonDerivedType(typeof(DashboardTarget.Giveaway), "giveaway")]
[JsonDerivedType(typeof(DashboardTarget.Guessing), "guessing")]
internal abstract record DashboardTarget
{
    private DashboardTarget() { }

    internal TResult Match<TResult>(
        Func<Automation, TResult> automation,
        Func<Value, TResult> value,
        Func<Countdown, TResult> countdown,
        Func<Poll, TResult> poll,
        Func<Prediction, TResult> prediction,
        Func<Giveaway, TResult> giveaway,
        Func<Guessing, TResult> guessing
    ) =>
        this switch
        {
            Automation target => automation(target),
            Value target => value(target),
            Countdown target => countdown(target),
            Poll target => poll(target),
            Prediction target => prediction(target),
            Giveaway target => giveaway(target),
            Guessing target => guessing(target),
            _ => throw new UnreachableException(),
        };

    internal sealed record Automation(AutomationFlowId FlowId, AutomationNodeId SourceNodeId)
        : DashboardTarget;

    internal sealed record Value(int DefinitionId) : DashboardTarget;

    internal sealed record Countdown(string Name) : DashboardTarget;

    internal sealed record Poll(int TemplateId) : DashboardTarget;

    internal sealed record Prediction(int TemplateId) : DashboardTarget;

    internal sealed record Giveaway : DashboardTarget;

    internal sealed record Guessing(int ProfileId) : DashboardTarget;
}
