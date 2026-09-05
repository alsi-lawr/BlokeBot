using BlokeBot.Core.Features.ViewerPortal;
using BlokeBot.Core.Features.ViewerPortal.Boundary;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotViewerPortal(this IServiceCollection services)
    {
        _ = ViewerPortalCatalogue.Descriptors;
        _ = services.AddSingleton<PublicDocumentProtector>();
        _ = services.AddSingleton<PublicViewerAdmission>();
        _ = services.AddScoped<PublicViewerGate>();
        _ = services.AddScoped<PublicViewerCircuit>();
        _ = services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>(
            provider => provider.GetRequiredService<PublicViewerCircuit>()
        );
        _ = services.AddSingleton<ViewerPortalAccess>();
        _ = services.AddSingleton<PortalActivityProjectors>();
        _ = services.AddSingleton<PortalDirectoryProjectors>();
        _ = services.AddSingleton<PortalPersonalProjectors>();
        _ = services.AddSingleton<PortalProjectors>();
        _ = services.AddScoped<ViewerPortalCatalogueService>();
        _ = services.AddScoped<PortalPersonalReader>();
        _ = services.AddSingleton<PortalReadTelemetry>();
        _ = services.AddSingleton<PortalProjectionRunner>();
        _ = services.AddScoped<PortalReadScheduler>();
        _ = services.AddScoped<PortalCircuitConnection>();
        _ = services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>(
            provider => provider.GetRequiredService<PortalCircuitConnection>()
        );
        return services;
    }
}
