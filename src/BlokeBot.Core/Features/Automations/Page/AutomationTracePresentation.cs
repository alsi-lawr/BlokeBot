using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

public sealed record AutomationTraceRow(
    AutomationTraceEntry Entry,
    AutomationNodeId? RootNodeId,
    string Context,
    AutomationSubflowRevisionId? RevisionId
);

public sealed class AutomationTracePresentation
{
    public ImmutableArray<AutomationTraceRow> Rows { get; }
    private readonly ImmutableDictionary<int, AutomationTraceRow> _bySequence;
    public ImmutableArray<AutomationScenarioNodeOutcome> RootOutcomes { get; }

    private AutomationTracePresentation(ImmutableArray<AutomationTraceRow> rows)
    {
        Rows = rows;
        _bySequence = rows.ToImmutableDictionary(row => row.Entry.Sequence);
        RootOutcomes =
        [
            .. rows.Where(row =>
                    row.Entry.Event.Invocation.ParentId is null
                    && row.Entry.Event.Node is not null
                    && row.Entry.Event.Kind == AutomationTraceEventKind.Result
                )
                .GroupBy(row => row.Entry.Event.Node!.Id)
                .Select(group => group.Last().Entry.Event)
                .Where(data =>
                    data.Outcome
                        is AutomationTraceOutcome.Succeeded
                            or AutomationTraceOutcome.Failed
                            or AutomationTraceOutcome.ContinuedAfterFailure
                            or AutomationTraceOutcome.Invalidated
                )
                .Select(data => new AutomationScenarioNodeOutcome(
                    data.Node!.Id,
                    data.Outcome switch
                    {
                        AutomationTraceOutcome.Failed => AutomationNodeRunState.Failed,
                        AutomationTraceOutcome.ContinuedAfterFailure =>
                            AutomationNodeRunState.ContinuedAfterFailure,
                        AutomationTraceOutcome.Invalidated => AutomationNodeRunState.Invalidated,
                        _ => AutomationNodeRunState.Succeeded,
                    },
                    data.Outcome == AutomationTraceOutcome.ContinuedAfterFailure
                        ? "Continued after failure"
                        : data.Outcome.ToString()
                )),
        ];
    }

    public AutomationTraceRow? Find(int sequence) => _bySequence.GetValueOrDefault(sequence);

    public static AutomationTracePresentation Create(AutomationTraceSnapshot trace)
    {
        var callers = new Dictionary<Guid, AutomationTraceNode>();
        var invocations = new Dictionary<Guid, (AutomationNodeId? RootNode, string Path)>();
        var rows = ImmutableArray.CreateBuilder<AutomationTraceRow>(trace.Events.Length);
        var occurrence = 0;
        foreach (var entry in trace.Events)
        {
            var data = entry.Event;
            var invocation = data.Invocation;
            if (
                data.Kind == AutomationTraceEventKind.Attempt
                && data.Node?.DefinitionId.Value == AutomationSubflowDefinitions.Invoke
            )
            {
                callers[invocation.Id] = data.Node;
            }
            if (
                data.Kind == AutomationTraceEventKind.SubflowEntry
                && !invocations.ContainsKey(invocation.Id)
            )
            {
                var parent = invocation.ParentId;
                var caller = parent is { } parentId ? callers.GetValueOrDefault(parentId) : null;
                var nested =
                    parent is { } id && invocations.TryGetValue(id, out var ancestor)
                        ? ancestor
                        : (default(AutomationNodeId?), string.Empty);
                var root = parent == trace.Id.Value ? caller?.Id : nested.Item1;
                var path = $"Invocation {++occurrence}";
                if (nested.Item2.Length > 0)
                {
                    path = $"{nested.Item2} › {path}";
                }
                invocations[invocation.Id] = (root, path);
                if (parent is { } consumed)
                {
                    _ = callers.Remove(consumed);
                }
            }
        }
        foreach (var entry in trace.Events)
        {
            var data = entry.Event;
            var invocation = data.Invocation;
            var context = invocations.GetValueOrDefault(invocation.Id);
            var isRoot = invocation.Id == trace.Id.Value && invocation.ParentId is null;
            rows.Add(
                new(
                    entry,
                    isRoot ? data.Node?.Id : context.RootNode,
                    isRoot ? "Root" : context.Path ?? $"Invocation {invocation.Id:N}",
                    invocation.RevisionId is { } revision ? new(revision) : null
                )
            );
        }
        return new(rows.ToImmutable());
    }
}
