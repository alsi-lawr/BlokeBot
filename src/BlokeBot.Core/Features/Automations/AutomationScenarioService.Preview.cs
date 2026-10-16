using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

public sealed record AutomationScenarioPreviewValue(
    AutomationPortId PortId,
    AutomationPortValueType ValueType,
    string DisplayValue
);

public sealed record AutomationScenarioEventPreview(
    int Sequence,
    long VirtualOffsetMilliseconds,
    ImmutableArray<AutomationScenarioPreviewValue> Values,
    string? WouldSend = null
);

public sealed partial class AutomationScenarioService
{
    private sealed class PreviewCapture(DateTime start)
    {
        private readonly ImmutableArray<AutomationScenarioEventPreview>.Builder _events =
            ImmutableArray.CreateBuilder<AutomationScenarioEventPreview>();
        private int _sequence;
        private int _bytes;

        internal ImmutableArray<AutomationScenarioEventPreview> Events => _events.ToImmutable();

        internal void Record(
            DateTime now,
            IReadOnlyDictionary<AutomationPortId, AutomationResolvedValue>? values,
            ImmutableArray<AutomationPortMetadata> ports,
            string? message
        )
        {
            var sequence = ++_sequence;
            if (
                sequence > AutomationTraceStore.MaximumEvents
                || _bytes >= AutomationTraceStore.MaximumBytes
            )
            {
                return;
            }
            ImmutableArray<AutomationScenarioPreviewValue> diagnostics = values is null
                ? []
                :
                [
                    .. AutomationDataValueSerialization
                        .Diagnostics(
                            values.ToDictionary(
                                pair => pair.Key,
                                pair =>
                                    pair.Value with
                                    {
                                        ValueFreeDiagnostic =
                                            !ports.Any(port =>
                                                port.Id == pair.Key
                                                && port.Sensitivity
                                                    == AutomationDataSensitivity.Safe
                                            )
                                            || pair.Value.Provenance.IsDefaultOrEmpty
                                            || !pair.Value.Provenance.All(Enum.IsDefined),
                                    }
                            )
                        )
                        .Select(value => new AutomationScenarioPreviewValue(
                            value.PortId,
                            value.ValueType,
                            value.DisplayValue
                        )),
                ];
            var wouldSend = message is null
                ? null
                : AutomationDataValueSerialization
                    .Diagnostics(
                        new Dictionary<AutomationPortId, AutomationResolvedValue>
                        {
                            [new("message")] = new(
                                new AutomationValue.Text(message),
                                [AutomationValueProvenance.Generated]
                            ),
                        }
                    )[0]
                    .DisplayValue;
            var preview = new AutomationScenarioEventPreview(
                sequence,
                (long)(now - start).TotalMilliseconds,
                diagnostics,
                wouldSend
            );
            var bytes = JsonSerializer.SerializeToUtf8Bytes(preview).Length;
            if (_bytes + bytes > AutomationTraceStore.MaximumBytes)
            {
                _bytes = AutomationTraceStore.MaximumBytes;
                return;
            }
            _bytes += bytes;
            _events.Add(preview);
        }
    }
}
