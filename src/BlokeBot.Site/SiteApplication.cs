using BlokeBot.Site.Components;

namespace BlokeBot.Site;

internal static class SiteApplication
{
    public static WebApplication Build(string[] arguments)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = arguments,
                ApplicationName = typeof(SiteApplication).Assembly.GetName().Name,
            }
        );
        builder.Services.AddRazorComponents();

        var app = builder.Build();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().DisableAntiforgery();
        return app;
    }
}
