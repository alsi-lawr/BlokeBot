using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PluginWorkerMessage), TypeInfoPropertyName = "WorkerMessage")]
[JsonSerializable(typeof(PluginLiveInvocation.Migration), TypeInfoPropertyName = "LiveMigration")]
[JsonSerializable(
    typeof(PluginInvocationContext.Migration),
    TypeInfoPropertyName = "ContextMigration"
)]
[JsonSerializable(typeof(PluginLiveInvocation.Page), TypeInfoPropertyName = "LivePage")]
[JsonSerializable(typeof(PluginInvocationContext.Page), TypeInfoPropertyName = "ContextPage")]
[JsonSerializable(typeof(PluginLiveInvocation.Automation), TypeInfoPropertyName = "LiveAutomation")]
[JsonSerializable(
    typeof(PluginInvocationContext.Automation),
    TypeInfoPropertyName = "ContextAutomation"
)]
[JsonSerializable(typeof(PluginValue.Boolean), TypeInfoPropertyName = "PluginBoolean")]
[JsonSerializable(typeof(PluginValue.String), TypeInfoPropertyName = "PluginString")]
[JsonSerializable(typeof(PluginHostCallOutcome.Returned), TypeInfoPropertyName = "HostReturned")]
[JsonSerializable(
    typeof(PluginWorkerInvocationOutcome.Returned),
    TypeInfoPropertyName = "WorkerReturned"
)]
[JsonSerializable(typeof(PluginHostCallOutcome.Failed), TypeInfoPropertyName = "HostFailed")]
[JsonSerializable(
    typeof(PluginWorkerInvocationOutcome.Failed),
    TypeInfoPropertyName = "WorkerFailed"
)]
[JsonSerializable(typeof(PluginHostCallOutcome.Cancelled), TypeInfoPropertyName = "HostCancelled")]
[JsonSerializable(
    typeof(PluginWorkerInvocationOutcome.Cancelled),
    TypeInfoPropertyName = "WorkerCancelled"
)]
public sealed partial class PluginWorkerJsonContext : JsonSerializerContext;
