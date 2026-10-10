using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardPredictionControls(
    DashboardAuthority authority,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IPredictionDashboardOperations predictions
)
{
    internal async Task<DashboardResult<PredictionControlState>> ReadAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Prediction configuration,
        CancellationToken ct
    ) =>
        await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected denied
            ? new DashboardResult<PredictionControlState>.Rejected(denied.Reason)
        : configuration.Binding
            is DashboardBinding.Configured { Target: DashboardTarget.Prediction target }
            ? new DashboardResult<PredictionControlState>.Succeeded(
                await predictions.ReadControlAsync(host.Value, target.TemplateId, ct)
            )
        : new DashboardResult<PredictionControlState>.Rejected(DashboardRejection.Invalid);

    internal Task<DashboardResult<PredictionOperationOutcome>> StartAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Prediction configuration,
        CancellationToken ct
    ) =>
        InvokeAsync(
            session,
            host,
            configuration,
            id => predictions.StartAsync(host.Value, id, ct),
            ct
        );

    internal Task<DashboardResult<PredictionOperationOutcome>> LockAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Prediction configuration,
        bool confirmed,
        CancellationToken ct
    ) =>
        InvokeAsync(
            session,
            host,
            configuration,
            _ => predictions.LockAsync(host.Value, confirmed, ct),
            ct
        );

    internal Task<DashboardResult<PredictionOperationOutcome>> CancelAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Prediction configuration,
        bool confirmed,
        CancellationToken ct
    ) =>
        InvokeAsync(
            session,
            host,
            configuration,
            _ => predictions.CancelAsync(host.Value, confirmed, ct),
            ct
        );

    internal Task<DashboardResult<PredictionOperationOutcome>> ResolveAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Prediction configuration,
        string winningOutcomeId,
        bool confirmed,
        CancellationToken ct
    ) =>
        InvokeAsync(
            session,
            host,
            configuration,
            _ => predictions.ResolveAsync(host.Value, winningOutcomeId, confirmed, ct),
            ct
        );

    private async Task<DashboardResult<PredictionOperationOutcome>> InvokeAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Prediction configuration,
        Func<int, Task<PredictionOperationOutcome>> operation,
        CancellationToken ct
    )
    {
        if (
            await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected denied
        )
        {
            return new DashboardResult<PredictionOperationOutcome>.Rejected(denied.Reason);
        }
        if (
            configuration.Binding
            is not DashboardBinding.Configured { Target: DashboardTarget.Prediction target }
        )
        {
            return new DashboardResult<PredictionOperationOutcome>.Succeeded(
                new PredictionOperationOutcome.TemplateNotFound()
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return new DashboardResult<PredictionOperationOutcome>.Succeeded(
            await db.TwitchPredictionTemplates.AnyAsync(
                x => x.HostId == host.Value && x.Id == target.TemplateId,
                ct
            )
                ? await operation(target.TemplateId)
                : new PredictionOperationOutcome.TemplateNotFound()
        );
    }
}
