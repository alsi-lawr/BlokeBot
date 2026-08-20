namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationNodeInspector
{
    protected override void OnParametersSet()
    {
        if (_rejectedRenameNodeId is { } nodeId && Node?.Id != nodeId)
        {
            _rejectedRenameNodeId = null;
            _rejectedRenamePortId = null;
            _renameFailure = null;
        }

        if (_invalidFixedValueNodeId is { } fixedValueNodeId && Node?.Id != fixedValueNodeId)
        {
            _invalidFixedValueNodeId = null;
            _invalidFixedValuePortId = null;
        }
    }

    private async Task AddTransformInputAsync()
    {
        Node?.AddTransformInput();
        await Changed.InvokeAsync();
    }

    private async Task AddTransformOutputAsync()
    {
        Node?.AddTransformOutput();
        await Changed.InvokeAsync();
    }

    private async Task RemoveTransformInputAsync(AutomationPortId portId)
    {
        Node?.RemoveTransformInput(portId);
        await Changed.InvokeAsync();
    }

    private async Task RemoveTransformOutputAsync(AutomationPortId portId)
    {
        Node?.RemoveTransformOutput(portId);
        await Changed.InvokeAsync();
    }

    private async Task UpdateTransformInputAsync(
        AutomationCelTransformInput input,
        string? displayName = null,
        string? valueType = null,
        string? nullability = null
    )
    {
        if (Node is null)
        {
            return;
        }
        _ = Enum.TryParse(valueType, out AutomationPortValueType parsedType);
        _ = Enum.TryParse(nullability, out AutomationPortNullability parsedNullability);
        Node.UpdateTransformInput(
            input.PortId,
            displayName ?? input.DisplayName,
            valueType is null ? input.ValueType : parsedType,
            nullability is null ? input.Nullability : parsedNullability
        );
        await Changed.InvokeAsync();
    }

    private async Task RenameTransformInputAsync(
        AutomationCelTransformInput input,
        string? identifier
    )
    {
        if (Node is null)
        {
            return;
        }
        var outcome = Node.RenameTransformInput(input.PortId, identifier ?? string.Empty);
        if (outcome is not AutomationTransformInputRenameOutcome.Succeeded)
        {
            _rejectedRenameNodeId = Node.Id;
            _rejectedRenamePortId = input.PortId;
            _renameFailure = outcome;
            _renameAttempt++;
            return;
        }
        _rejectedRenameNodeId = null;
        _rejectedRenamePortId = null;
        _renameFailure = null;
        await Changed.InvokeAsync();
    }

    private string? RenameDiagnostic(AutomationPortId portId) =>
        Node is not null && _rejectedRenameNodeId == Node.Id && _rejectedRenamePortId == portId
            ? _renameFailure switch
            {
                AutomationTransformInputRenameOutcome.InvalidIdentifier =>
                    "Use a unique CEL name. Letters, digits and underscores only, not starting with a digit, and not a reserved word.",
                AutomationTransformInputRenameOutcome.InvalidOutput =>
                    "Finish or repair every output expression before renaming this input.",
                _ => null,
            }
            : null;

    private async Task UpdateTransformOutputAsync(
        AutomationCelTransformOutput output,
        string? displayName = null,
        string? valueType = null,
        string? nullability = null,
        string? source = null
    )
    {
        if (Node is null)
        {
            return;
        }
        _ = Enum.TryParse(valueType, out AutomationPortValueType parsedType);
        _ = Enum.TryParse(nullability, out AutomationPortNullability parsedNullability);
        Node.UpdateTransformOutput(
            output.PortId,
            displayName ?? output.DisplayName,
            valueType is null ? output.ValueType : parsedType,
            nullability is null ? output.Nullability : parsedNullability,
            source ?? output.Source
        );
        await Changed.InvokeAsync();
    }
}
