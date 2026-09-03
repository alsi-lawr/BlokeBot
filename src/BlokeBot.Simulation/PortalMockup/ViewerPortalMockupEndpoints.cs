using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Simulation.PortalMockup;

/// <summary>The Simulation-only viewer portal mockup route.</summary>
internal static class ViewerPortalMockupEndpoints
{
    public static void MapViewerPortalMockup(this WebApplication app) =>
        _ = app.MapGet(
                "/simulation/portal-mockup",
                static async (
                    string? state,
                    string? viewer,
                    string? theme,
                    HttpContext context,
                    ILoggerFactory loggerFactory
                ) =>
                {
                    if (
                        !Enum.TryParse<ViewerPortalMockupState>(
                            state ?? "populated",
                            true,
                            out var parsedState
                        )
                        || !Enum.TryParse<ViewerPortalMockupViewer>(
                            viewer ?? "anonymous",
                            true,
                            out var parsedViewer
                        )
                    )
                    {
                        return Results.BadRequest();
                    }

                    var page = ViewerPortalMockupFixture.Build(
                        parsedState,
                        parsedViewer,
                        string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase)
                            ? "dark"
                            : "light"
                    );
                    await using var renderer = new HtmlRenderer(
                        context.RequestServices,
                        loggerFactory
                    );
                    var html = await renderer.Dispatcher.InvokeAsync(async () =>
                    {
                        var output =
                            await renderer.RenderComponentAsync<ViewerPortalMockupDocument>(
                                ParameterView.FromDictionary(
                                    new Dictionary<string, object?> { ["Page"] = page }
                                )
                            );
                        return output.ToHtmlString();
                    });
                    return Results.Content(html, "text/html; charset=utf-8");
                }
            )
            .AllowAnonymous();
}
