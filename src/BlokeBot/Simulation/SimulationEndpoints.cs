using BlokeBot.Auth.Sessions;
using BlokeBot.Hosts;

namespace BlokeBot.Simulation;

internal static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this WebApplication app)
    {
        app.MapGet("/simulation/ready", () => Results.Ok()).AllowAnonymous();

        app.MapGet(
                "/simulation/login",
                async (
                    HttpContext context,
                    SimulationFixtureSeeder fixtures,
                    AuthSessionService sessions,
                    string? view,
                    string? theme,
                    CancellationToken cancellationToken
                ) =>
                {
                    var host = await fixtures.SeedAsync(cancellationToken);
                    await sessions.SignInAsync(
                        context,
                        new AuthenticatedUser(
                            SimulationMode.UserId,
                            SimulationMode.Login,
                            SimulationMode.DisplayName,
                            null,
                            [host],
                            true
                        ),
                        host.Id
                    );

                    var selectedTheme = string.Equals(
                        theme,
                        "dark",
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? "dark"
                        : "light";

                    return Results.Redirect(
                        $"{SimulationViewCatalog.PathFor(view)}?simulationTheme={selectedTheme}"
                    );
                }
            )
            .AllowAnonymous();
    }
}
