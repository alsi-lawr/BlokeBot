using BlokeBot.Commands;
using BlokeBot.Core.Features.Commands;

namespace BlokeBot.Simulation;

internal static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this WebApplication app)
    {
        _ = app.MapGet(
                "/simulation/ready",
                static (SimulationReadiness readiness) =>
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

        _ = app.MapGet(
                "/simulation/login",
                static (string? view, string? theme) =>
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

        _ = app.MapPost(
                "/simulation/commands/liveness/{state}",
                static async (
                    string state,
                    SimulationCommandCatalogScenario scenario,
                    CancellationToken ct
                ) =>
                {
                    await scenario.SetLivenessAsync(state, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/round/{state}",
                static async (
                    string state,
                    SimulationCommandCatalogScenario scenario,
                    CancellationToken ct
                ) =>
                {
                    await scenario.SetRoundAsync(state, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/giveaway/{state}",
                static async (
                    string state,
                    SimulationCommandCatalogScenario scenario,
                    CancellationToken ct
                ) =>
                {
                    await scenario.SetGiveawayAsync(state, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/features/{state}",
                static async (
                    string state,
                    SimulationCommandCatalogScenario scenario,
                    CancellationToken ct
                ) =>
                {
                    await scenario.SetFeatureAvailabilityAsync(state, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();
        _ = app.MapGet(
                "/simulation/commands/catalog",
                static async (
                    SimulationCommandCatalogScenario scenario,
                    ViewerCommandCatalogService catalog,
                    CancellationToken ct
                ) => Results.Json(await scenario.SnapshotAsync(catalog, ct))
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/chat",
                static async (
                    SimulationCommandCatalogScenario scenario,
                    ChatCommandDispatcher dispatcher,
                    CancellationToken ct
                ) => Results.Json(new { messages = await scenario.DispatchAsync(dispatcher, ct) })
            )
            .AllowAnonymous();
    }
}
