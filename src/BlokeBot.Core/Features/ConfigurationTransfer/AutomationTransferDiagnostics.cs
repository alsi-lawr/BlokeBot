using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal enum AutomationTransferDiagnosticKind
{
    Redaction,
    Invalid,
}

internal sealed record AutomationTransferDiagnostic(
    string Flow,
    string Node,
    string Reason,
    AutomationTransferDiagnosticKind Kind
);

internal static class AutomationTransferLabels
{
    internal const string NoNode = "node-none";

    internal static string Flow(int index) => $"flow-{index + 1:D4}";

    internal static string Node(int index) => $"node-{index + 1:D4}";
}

internal static class AutomationTransferDiagnostics
{
    internal static void LogExport(
        ILogger logger,
        int hostId,
        IEnumerable<AutomationTransferDiagnostic> diagnostics
    )
    {
        foreach (var diagnostic in Distinct(diagnostics))
        {
            if (diagnostic.Kind == AutomationTransferDiagnosticKind.Redaction)
            {
                logger.LogWarning(
                    "Configuration transfer exported a redacted Automation. HostId {HostId} Flow {Flow} Node {Node} Reason {Reason}",
                    hostId,
                    diagnostic.Flow,
                    diagnostic.Node,
                    diagnostic.Reason
                );
            }
            else
            {
                logger.LogWarning(
                    "Configuration transfer exported an invalid Automation. HostId {HostId} Flow {Flow} Node {Node} Reason {Reason}",
                    hostId,
                    diagnostic.Flow,
                    diagnostic.Node,
                    diagnostic.Reason
                );
            }
        }
    }

    internal static void LogImport(
        ILogger logger,
        int hostId,
        IEnumerable<AutomationTransferDiagnostic> diagnostics
    )
    {
        foreach (var diagnostic in Distinct(diagnostics))
        {
            if (diagnostic.Kind == AutomationTransferDiagnosticKind.Redaction)
            {
                logger.LogWarning(
                    "Configuration transfer imported a redacted Automation. HostId {HostId} Flow {Flow} Node {Node} Reason {Reason}",
                    hostId,
                    diagnostic.Flow,
                    diagnostic.Node,
                    diagnostic.Reason
                );
            }
            else
            {
                logger.LogWarning(
                    "Configuration transfer imported an invalid Automation. HostId {HostId} Flow {Flow} Node {Node} Reason {Reason}",
                    hostId,
                    diagnostic.Flow,
                    diagnostic.Node,
                    diagnostic.Reason
                );
            }
        }
    }

    private static IEnumerable<AutomationTransferDiagnostic> Distinct(
        IEnumerable<AutomationTransferDiagnostic> diagnostics
    ) => diagnostics.Distinct();
}

internal static class AutomationTransferPlaceholder
{
    private const string _propertyName = "format-1-placeholder";
    internal const string Identity = "identity-redacted";
    internal const string CustomCommand = "custom-command-reference-unmapped";
    internal const string Overlay = "overlay-reference-unmapped";
    internal const string CustomReward = "custom-reward-reference-unmapped";

    internal static JsonElement Create(string reason) =>
        JsonSerializer.SerializeToElement(new PlaceholderDocument(reason));

    internal static void Write(Utf8JsonWriter writer, string reason) =>
        Create(reason).WriteTo(writer);

    internal static bool Is(JsonElement value, string reason)
    {
        try
        {
            return value.Deserialize<PlaceholderDocument>()?.Reason == reason;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record PlaceholderDocument(
        [property: JsonRequired, JsonPropertyName(_propertyName)] string Reason
    );
}
