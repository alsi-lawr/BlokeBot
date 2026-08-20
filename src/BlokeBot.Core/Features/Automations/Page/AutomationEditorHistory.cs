using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations.Page;

internal enum AutomationEditorHistoryAction
{
    None,
    Undo,
    Redo,
}

internal static class AutomationEditorHistoryShortcut
{
    internal static AutomationEditorHistoryAction Parse(string action) =>
        action switch
        {
            "undo" => AutomationEditorHistoryAction.Undo,
            "redo" => AutomationEditorHistoryAction.Redo,
            _ => AutomationEditorHistoryAction.None,
        };
}

internal sealed class AutomationEditorHistory
{
    internal const int Capacity = 20;

    private readonly List<AutomationEditorDraftDiff> _undo = [];
    private readonly List<AutomationEditorDraftDiff> _redo = [];
    private AutomationEditorDraftSnapshot? _current;
    private AutomationEditorDraftSnapshot? _saved;

    internal int UndoCount => _undo.Count;

    internal int RedoCount => _redo.Count;

    internal void StartNew(AutomationEditorState editor) =>
        Reset(AutomationEditorDraftSnapshot.Capture(editor), null);

    internal void StartLoaded(AutomationEditorState editor)
    {
        var snapshot = AutomationEditorDraftSnapshot.Capture(editor);
        Reset(snapshot, snapshot);
    }

    internal void Clear() => Reset(null, null);

    internal void ContinueAfterSave(AutomationEditorState editor)
    {
        var snapshot = AutomationEditorDraftSnapshot.Capture(editor);
        _current = snapshot;
        _saved = snapshot;
    }

    internal bool IsDirty(AutomationEditorState editor) =>
        _saved is null || !_saved.Matches(editor);

    private void Reset(AutomationEditorDraftSnapshot? current, AutomationEditorDraftSnapshot? saved)
    {
        _undo.Clear();
        _redo.Clear();
        _current = current;
        _saved = saved;
    }

    internal bool Record(AutomationEditorState editor)
    {
        var next = AutomationEditorDraftSnapshot.Capture(editor);
        if (_current is null || _current.ContentEquals(next))
        {
            _current = next;
            return false;
        }

        Push(_undo, new(_current, next));
        _redo.Clear();
        _current = next;
        return true;
    }

    internal AutomationEditorState? Undo(AutomationEditorState editor)
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var diff = Pop(_undo);
        Push(_redo, diff);
        _current = diff.Before;
        return diff.Before.Restore(editor);
    }

    internal AutomationEditorState? Redo(AutomationEditorState editor)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var diff = Pop(_redo);
        Push(_undo, diff);
        _current = diff.After;
        return diff.After.Restore(editor);
    }

    private static AutomationEditorDraftDiff Pop(List<AutomationEditorDraftDiff> stack)
    {
        var index = stack.Count - 1;
        var diff = stack[index];
        stack.RemoveAt(index);
        return diff;
    }

    private static void Push(List<AutomationEditorDraftDiff> stack, AutomationEditorDraftDiff diff)
    {
        if (stack.Count == Capacity)
        {
            stack.RemoveAt(0);
        }

        stack.Add(diff);
    }
}

internal sealed record AutomationEditorDraftDiff(
    AutomationEditorDraftSnapshot Before,
    AutomationEditorDraftSnapshot After
);

internal sealed class AutomationEditorDraftSnapshot
{
    private readonly AutomationFlowDraft _draft;
    private readonly ImmutableDictionary<
        AutomationNodeId,
        AutomationDefinitionDescriptor
    > _definitions;

    private AutomationEditorDraftSnapshot(
        AutomationFlowDraft draft,
        ImmutableDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions
    )
    {
        _draft = draft;
        _definitions = definitions;
    }

    internal static AutomationEditorDraftSnapshot Capture(AutomationEditorState editor) =>
        new(
            editor.Draft(default) with
            {
                Id = null,
                HostId = default,
                IsEnabled = false,
            },
            editor.Nodes.ToImmutableDictionary(
                static node => node.Id,
                static node => node.Definition
            )
        );

    internal bool Matches(AutomationEditorState editor) => ContentEquals(Capture(editor));

    internal bool ContentEquals(AutomationEditorDraftSnapshot other) =>
        DraftContentEquals(_draft, other._draft);

    internal AutomationEditorState Restore(AutomationEditorState current) =>
        AutomationEditorState.Restore(
            _draft with
            {
                Id = current.Id,
                IsEnabled = current.IsEnabled,
            },
            _definitions
        );

    private static bool DraftContentEquals(AutomationFlowDraft left, AutomationFlowDraft right) =>
        left.Id == right.Id
        && left.HostId == right.HostId
        && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
        && left.SchemaVersion == right.SchemaVersion
        && left.IsEnabled == right.IsEnabled
        && left.Canvas == right.Canvas
        && left.Edges.SequenceEqual(right.Edges)
        && NodesEqual(left.Nodes, right.Nodes);

    private static bool NodesEqual(
        ImmutableArray<AutomationFlowDraftNode> left,
        ImmutableArray<AutomationFlowDraftNode> right
    )
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!NodesEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool NodesEqual(AutomationFlowDraftNode left, AutomationFlowDraftNode right) =>
        left.Id == right.Id
        && string.Equals(left.Definition.TypeId, right.Definition.TypeId, StringComparison.Ordinal)
        && left.Definition.SchemaVersion == right.Definition.SchemaVersion
        && JsonElement.DeepEquals(left.Definition.Configuration, right.Definition.Configuration)
        && left.ExpressionLanguageVersion == right.ExpressionLanguageVersion
        && left.FailurePolicy == right.FailurePolicy
        && left.Position == right.Position
        && string.Equals(left.DisplayAlias, right.DisplayAlias, StringComparison.Ordinal)
        && BindingsEqual(left.InputBindings, right.InputBindings);

    private static bool BindingsEqual(
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding> left,
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding> right
    ) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var binding) && pair.Value == binding);
}
