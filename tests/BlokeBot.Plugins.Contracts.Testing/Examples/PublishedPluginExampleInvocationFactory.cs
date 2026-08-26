using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static class PublishedPluginExampleInvocationFactory
{
    internal static PluginLiveInvocation LiveInvocation(PublishedPluginExampleScenario scenario) =>
        scenario.InvocationKind switch
        {
            PublishedPluginExampleInvocationKind.Lifecycle => new PluginLiveInvocation.Lifecycle(
                scenario.Module,
                scenario.Operation,
                new PluginValue.Nil()
            ),
            PublishedPluginExampleInvocationKind.Migration => new PluginLiveInvocation.Migration(
                scenario.Module,
                scenario.Operation,
                new PluginValue.Nil()
            ),
            PublishedPluginExampleInvocationKind.Command => new PluginLiveInvocation.Command(
                scenario.Module,
                scenario.Operation,
                new PluginValue.Nil()
            ),
            PublishedPluginExampleInvocationKind.Event => new PluginLiveInvocation.Event(
                scenario.Module,
                scenario.Operation,
                new PluginValue.Nil()
            ),
            PublishedPluginExampleInvocationKind.Schedule => new PluginLiveInvocation.Schedule(
                scenario.Module,
                scenario.Operation,
                new PluginValue.Nil()
            ),
            PublishedPluginExampleInvocationKind.HostAction => new PluginLiveInvocation.HostAction(
                scenario.Module,
                scenario.Operation,
                new PluginValue.Nil()
            ),
            PublishedPluginExampleInvocationKind.Storage => new PluginLiveInvocation.Storage(
                scenario.Module,
                scenario.Operation,
                new PluginValue.Nil()
            ),
            PublishedPluginExampleInvocationKind.Page => new PluginLiveInvocation.Page(
                scenario.Module,
                scenario.Operation,
                new PluginValue.Nil()
            ),
            PublishedPluginExampleInvocationKind.Automation => new PluginLiveInvocation.Automation(
                scenario.Module,
                scenario.Operation,
                AutomationDefinitionId(),
                PluginAutomationDefinitionKind.Action,
                new PluginValue.Nil()
            ),
        };

    internal static PluginWorkerInvocationIdentity Identity(
        PreparedPluginWorkerPackage package,
        PublishedPluginExampleInvocationKind invocationKind
    )
    {
        var plugin = package.Descriptor.Plugin;
        var host = HostId();
        PluginInvocationContext context = invocationKind switch
        {
            PublishedPluginExampleInvocationKind.Lifecycle =>
                new PluginInvocationContext.Installation(plugin),
            PublishedPluginExampleInvocationKind.Migration => new PluginInvocationContext.Migration(
                plugin,
                MigrationId(),
                SemanticVersion("1.0.0"),
                plugin.Release.DeclaredVersion
            ),
            PublishedPluginExampleInvocationKind.Page => new PluginInvocationContext.Page(
                plugin,
                host,
                PageId(),
                PageSessionId()
            ),
            PublishedPluginExampleInvocationKind.Automation =>
                new PluginInvocationContext.Automation(
                    plugin,
                    host,
                    FeatureId(),
                    AutomationDefinitionId(),
                    AutomationInvocationId()
                ),
            _ => new PluginInvocationContext.Channel(plugin, host),
        };
        return new(
            plugin,
            FeatureId(),
            host,
            context,
            InvocationId(),
            CoroutineId(),
            Generation(),
            PluginWorkerDeadline.From(DateTimeOffset.UtcNow.AddSeconds(10)),
            CancellationId()
        );
    }

    private static PluginFeatureId FeatureId() =>
        PluginFeatureId.TryCreate("example", out var value) ? value : throw InvalidIdentifier();

    private static PluginMigrationId MigrationId() =>
        PluginMigrationId.TryCreate("example-migration", out var value)
            ? value
            : throw InvalidIdentifier();

    private static PluginPageId PageId() =>
        PluginPageId.TryCreate("example-page", out var value) ? value : throw InvalidIdentifier();

    private static PluginAutomationDefinitionId AutomationDefinitionId() =>
        PluginAutomationDefinitionId.TryCreate("example-action", out var value)
            ? value
            : throw InvalidIdentifier();

    private static PluginHostId HostId() =>
        PluginHostId.TryCreate(1, out var value) ? value : throw InvalidIdentifier();

    private static PluginPageSessionId PageSessionId() =>
        PluginPageSessionId.TryCreate(Guid.NewGuid(), out var value)
            ? value
            : throw InvalidIdentifier();

    private static PluginAutomationInvocationId AutomationInvocationId() =>
        PluginAutomationInvocationId.TryCreate(Guid.NewGuid(), out var value)
            ? value
            : throw InvalidIdentifier();

    private static PluginWorkerInvocationId InvocationId() =>
        PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var value)
            ? value
            : throw InvalidIdentifier();

    private static PluginCoroutineId CoroutineId() =>
        PluginCoroutineId.TryCreate(Guid.NewGuid(), out var value)
            ? value
            : throw InvalidIdentifier();

    private static PluginWorkerGeneration Generation() =>
        PluginWorkerGeneration.TryCreate(1, out var value) ? value : throw InvalidIdentifier();

    private static PluginWorkerCancellationId CancellationId() =>
        PluginWorkerCancellationId.TryCreate(Guid.NewGuid(), out var value)
            ? value
            : throw InvalidIdentifier();

    private static SemanticVersion SemanticVersion(string value) =>
        BlokeBot.Plugins.Contracts.SemanticVersion.TryCreate(value, out var version)
            ? version
            : throw InvalidIdentifier();

    private static InvalidOperationException InvalidIdentifier() =>
        new("The example harness could not create a canonical fixture identifier.");
}
