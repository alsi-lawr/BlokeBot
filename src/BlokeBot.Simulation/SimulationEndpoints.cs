namespace BlokeBot.Simulation;

internal static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/simulation/ready",
                (SimulationReadiness readiness) =>
                {
                    var projection = readiness.Project();
                    return Results.Json(
                        projection,
                        statusCode: projection.Ready
                            ? StatusCodes.Status200OK
                            : StatusCodes.Status503ServiceUnavailable
                    );
                }
            )
            .AllowAnonymous();

        app.MapGet(
                "/simulation/login",
                (string? view, string? theme) =>
                {
                    var selectedTheme = string.Equals(
                        theme,
                        "dark",
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? "dark"
                        : "light";
                    var returnUrl =
                        $"{SimulationViewCatalog.PathFor(view)}?simulationTheme={selectedTheme}";
                    return Results.Redirect(
                        $"/auth/login?start=true&returnUrl={Uri.EscapeDataString(returnUrl)}"
                    );
                }
            )
            .AllowAnonymous();
    }
}
