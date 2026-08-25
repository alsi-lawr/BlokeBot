using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.BotRuntime;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Persistence;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotApplication
{
    public static async Task InitializeBlokeBotPersistenceAsync(
        this WebApplication app,
        CancellationToken cancellationToken
    )
    {
        await app
            .Services.GetRequiredService<BlokeBotDatabaseInitializer>()
            .InitializeAsync(cancellationToken);
        await app
            .Services.GetRequiredService<HostedChannelRuntimeLifecycleService>()
            .RecoverInterruptedStopsAsync(cancellationToken);
    }

    public static WebApplication UseBlokeBotCore(
        this WebApplication app,
        BlokeBotRuntimeMode runtime
    )
    {
        app.UseOverlayAccessLogRedaction();

        if (!app.Environment.IsDevelopment())
        {
            _ = app.UseExceptionHandler("/Error", createScopeForErrors: true);
            _ = app.UseHsts();
        }

        _ = app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        _ = app.UseHttpsRedirection();
        _ = app.UseAntiforgery();
        _ = app.UseAuthentication();
        _ = app.UseAuthorization();

        app.MapOverlayBrowserSourceEndpoints();
        app.MapPluginWebEndpoints();
        _ = app.MapMethods(
            "/favicon.ico",
            ["GET", "HEAD"],
            static () => Results.Redirect("/blokebot-mark.svg")
        );
        _ = app.UseStaticFiles();
        _ = app.MapStaticAssets();
        _ = app.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireAuthorization();
        app.MapAuthEndpoints();
        if (runtime == BlokeBotRuntimeMode.Online)
        {
            app.MapEventSubWebhookEndpoint();
        }
        if (runtime == BlokeBotRuntimeMode.Online)
        {
            app.MapBotOAuthEndpoints();
        }
        else
        {
            app.MapUnavailableBotOAuthEndpoint();
        }

        app.MapHostConfigEndpoints();
        app.MapConfigurationTransferEndpoints();
        app.MapViewerPassportEndpoints();
        return app;
    }
}
