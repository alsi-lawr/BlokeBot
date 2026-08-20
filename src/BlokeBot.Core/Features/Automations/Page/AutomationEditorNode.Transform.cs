using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

public sealed partial class AutomationEditorNode
{
    internal void AddTransformInput()
    {
        if (_transform is null)
        {
            return;
        }

        var sequence = NextTransformSequence(
            _transform.Inputs.Select(static input => input.Identifier.Value),
            "input"
        );
        var input = new AutomationCelTransformInput(
            new($"input-{Guid.NewGuid():N}"),
            new(sequence),
            $"Input {_transform.Inputs.Length + 1}",
            new($"binding-{Guid.NewGuid():N}"),
            AutomationPortValueType.Text,
            AutomationPortNullability.NonNullable,
            new AutomationValue.Text(string.Empty)
        );
        _transform = _transform with { Inputs = _transform.Inputs.Add(input) };
        _values[input.BindingFieldId] = string.Empty;
        _bindings[input.BindingFieldId] = new(AutomationInputBindingMode.Fixed, null);
        RefreshTransformDefinition();
    }

    internal void AddTransformOutput()
    {
        if (_transform is null)
        {
            return;
        }

        var inputIdentifier = _transform.Inputs.FirstOrDefault()?.Identifier.Value;
        var output = new AutomationCelTransformOutput(
            new($"output-{Guid.NewGuid():N}"),
            $"Output {_transform.Outputs.Length + 1}",
            AutomationPortValueType.Text,
            AutomationPortNullability.NonNullable,
            inputIdentifier ?? "\"\""
        );
        _transform = _transform with { Outputs = _transform.Outputs.Add(output) };
        RefreshTransformDefinition();
    }

    internal void RemoveTransformInput(AutomationPortId portId)
    {
        if (
            _transform is null
            || _transform.Inputs.FirstOrDefault(input => input.PortId == portId) is not { } input
        )
        {
            return;
        }

        _transform = _transform with { Inputs = _transform.Inputs.Remove(input) };
        _ = _values.Remove(input.BindingFieldId);
        _ = _bindings.Remove(input.BindingFieldId);
        RefreshTransformDefinition();
    }

    internal void RemoveTransformOutput(AutomationPortId portId)
    {
        if (
            _transform is null
            || _transform.Outputs.FirstOrDefault(output => output.PortId == portId)
                is not { } output
        )
        {
            return;
        }

        _transform = _transform with { Outputs = _transform.Outputs.Remove(output) };
        RefreshTransformDefinition();
    }

    internal void UpdateTransformInput(
        AutomationPortId portId,
        string displayName,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    )
    {
        if (_transform is null)
        {
            return;
        }

        var index = IndexOf(_transform.Inputs, input => input.PortId == portId);
        if (index < 0)
        {
            return;
        }

        var current = _transform.Inputs[index];
        var fixedValue = ParseFixedValue(_values[current.BindingFieldId], valueType, nullability);
        _transform = _transform with
        {
            Inputs = _transform.Inputs.SetItem(
                index,
                current with
                {
                    DisplayName = displayName,
                    ValueType = valueType,
                    Nullability = nullability,
                    FixedValue = fixedValue,
                }
            ),
        };
        _values[current.BindingFieldId] = DisplayFixedValue(fixedValue);
        RefreshTransformDefinition();
    }

    internal AutomationTransformInputRenameOutcome RenameTransformInput(
        AutomationPortId portId,
        string identifier
    )
    {
        if (_transform is null)
        {
            return AutomationTransformInputRenameOutcome.InvalidIdentifier;
        }

        var index = IndexOf(_transform.Inputs, input => input.PortId == portId);
        if (index < 0)
        {
            return AutomationTransformInputRenameOutcome.InvalidIdentifier;
        }

        var current = _transform.Inputs[index];
        if (string.Equals(identifier, current.Identifier.Value, StringComparison.Ordinal))
        {
            return AutomationTransformInputRenameOutcome.Succeeded;
        }

        if (
            !AutomationCelSyntax.IsIdentifier(identifier)
            || AutomationCelSyntax.ReservedIdentifiers.Contains(identifier)
            || _transform.Inputs.Any(input =>
                string.Equals(input.Identifier.Value, identifier, StringComparison.Ordinal)
            )
        )
        {
            return AutomationTransformInputRenameOutcome.InvalidIdentifier;
        }

        var rewrittenOutputs = ImmutableArray.CreateBuilder<AutomationCelTransformOutput>(
            _transform.Outputs.Length
        );
        foreach (var output in _transform.Outputs)
        {
            if (
                AutomationTransformCelService.RenameIdentifier(
                    output,
                    current.Identifier.Value,
                    identifier
                )
                is not AutomationCelIdentifierRewrite.Success rewrite
            )
            {
                return AutomationTransformInputRenameOutcome.InvalidOutput;
            }

            rewrittenOutputs.Add(output with { Source = rewrite.Source });
        }

        _transform = _transform with
        {
            Inputs = _transform.Inputs.SetItem(
                index,
                current with
                {
                    Identifier = new(identifier),
                }
            ),
            Outputs = rewrittenOutputs.MoveToImmutable(),
        };
        RefreshTransformDefinition();
        return AutomationTransformInputRenameOutcome.Succeeded;
    }

    internal void UpdateTransformOutput(
        AutomationPortId portId,
        string displayName,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability,
        string source
    )
    {
        if (_transform is null)
        {
            return;
        }

        var index = IndexOf(_transform.Outputs, output => output.PortId == portId);
        if (index < 0)
        {
            return;
        }

        _transform = _transform with
        {
            Outputs = _transform.Outputs.SetItem(
                index,
                _transform.Outputs[index] with
                {
                    DisplayName = displayName,
                    ValueType = valueType,
                    Nullability = nullability,
                    Source = source,
                }
            ),
        };
        RefreshTransformDefinition();
    }
}
