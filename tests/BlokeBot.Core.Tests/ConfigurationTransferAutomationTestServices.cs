using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;

namespace BlokeBot.Core.Tests;

internal sealed record ConfigurationTransferAutomationTestServices(
    AutomationCatalogService Catalog,
    AutomationFlowService Flows
)
{
    internal static ConfigurationTransferAutomationTestServices Create(
        SqliteBlokeBotDbFactory database
    )
    {
        var features = TestHostFeatureServices.Create(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );
        var catalog = new AutomationCatalogService(
            new([new CoreAutomationCatalogModule(), new TwitchEventAutomationCatalogModule()]),
            features
        );
        return new(
            catalog,
            new(
                database,
                catalog,
                new(),
                UnavailableOverlayCueAdmissionService.Instance,
                TimeProvider.System
            )
        );
    }

    private sealed class UnavailableOverlayCueAdmissionService : IOverlayCueAdmissionService
    {
        internal static UnavailableOverlayCueAdmissionService Instance { get; } = new();

        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        ) => Unavailable<OverlayCueReferenceOutcome>();

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Unavailable<OverlayCueAdmissionCatalog>();

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) => Unavailable<OverlayCueAdmissionOutcome>();

        private static Task<T> Unavailable<T>() =>
            Task.FromException<T>(new InvalidOperationException("Overlay Cues are unavailable."));
    }
}
