using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Persistence;
using BlokeBot.Simulation.FakeTwitch;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Simulation;

internal static partial class SimulationAutomationAuthoringFixture
{
    internal static void MapEvidence(WebApplication app)
    {
        if (Environment.GetEnvironmentVariable("BLOKEBOT_AUTOMATION_AUTHORING_FIXTURE") != "1")
        {
            return;
        }
        _ = app.MapGet(
                "/simulation/automation-authoring/evidence",
                async (
                    HttpContext context,
                    IDbContextFactory<BlokeBotDbContext> factory,
                    FakeTwitchAuthority authority,
                    CancellationToken ct
                ) =>
                {
                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (
                        !session.CanManageSelectedHostConfig
                        || session.State is not AuthSessionState.Selected selected
                    )
                    {
                        return Results.Forbid();
                    }
                    await using var db = await factory.CreateDbContextAsync(ct);
                    var runs = await db
                        .AutomationFlowRuns.AsNoTracking()
                        .Where(r =>
                            r.HostId == selected.Selection.Current.Id
                            && r.Flow.Name.EndsWith("verification")
                        )
                        .OrderByDescending(r => r.StartedAtUtc)
                        .Take(20)
                        .Select(r => new
                        {
                            r.Id,
                            Flow = r.Flow.Name,
                            r.SourceDefinitionId,
                            r.SourceOccurrenceId,
                            r.Status,
                            r.ContextJson,
                        })
                        .ToArrayAsync(ct);
                    return Results.Json(
                        new
                        {
                            runs,
                            chat = authority
                                .Transcript.Where(t => t.Kind == "helix.chat.message")
                                .TakeLast(20)
                                .ToArray(),
                        }
                    );
                }
            )
            .RequireAuthorization("HostSelected");
    }
}
