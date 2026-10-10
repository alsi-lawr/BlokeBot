using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardBindingReader(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    DashboardAuthority authority
)
{
    internal async Task<DashboardResult<IReadOnlyList<DashboardWidgetBindingState>>> ReadAsync(
        AuthenticatedSession session,
        DashboardHostId hostId,
        DashboardDocument document,
        CancellationToken ct
    )
    {
        var authorization = await authority.AuthorizeAsync(session, hostId, ct);
        if (authorization is DashboardResult<DashboardHostId>.Rejected denied)
        {
            return new DashboardResult<IReadOnlyList<DashboardWidgetBindingState>>.Rejected(
                denied.Reason
            );
        }
        if (!DashboardDocuments.Valid(document))
        {
            return new DashboardResult<IReadOnlyList<DashboardWidgetBindingState>>.Rejected(
                DashboardRejection.Invalid
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var features = await db
            .Hosts.Where(host => host.Id == hostId.Value)
            .Select(host => (HostFeatureFlags?)host.EnabledFeatures)
            .SingleOrDefaultAsync(ct);
        if (features is null)
        {
            return new DashboardResult<IReadOnlyList<DashboardWidgetBindingState>>.Rejected(
                DashboardRejection.Missing
            );
        }
        var states = new List<DashboardWidgetBindingState>();
        foreach (var widget in document.Widgets)
        {
            var state = await widget.Configuration.Binding.Match(
                bound =>
                    AvailabilityAsync(
                        db,
                        hostId,
                        features.Value,
                        widget.Configuration,
                        bound.Target,
                        ct
                    ),
                _ => Task.FromResult(DashboardBindingAvailability.Unconfigured)
            );
            states.Add(new(widget.Id, state));
        }
        return new DashboardResult<IReadOnlyList<DashboardWidgetBindingState>>.Succeeded(states);
    }

    private static async Task<DashboardBindingAvailability> AvailabilityAsync(
        BlokeBotDbContext db,
        DashboardHostId hostId,
        HostFeatureFlags features,
        DashboardWidgetConfiguration configuration,
        DashboardTarget target,
        CancellationToken ct
    )
    {
        var required = target.Match(
            _ => HostFeatureFlags.Automations,
            _ => HostFeatureFlags.CustomCommands,
            _ => HostFeatureFlags.Automations,
            _ => HostFeatureFlags.Polls,
            _ => HostFeatureFlags.Predictions,
            _ => HostFeatureFlags.Points,
            _ => HostFeatureFlags.Guessing
        );
        if ((features & required) != required)
        {
            return DashboardBindingAvailability.FeatureDisabled;
        }
        var exists = await target.Match(
            automation =>
                db.AutomationFlowNodes.AnyAsync(
                    node =>
                        node.Id == automation.SourceNodeId.Value
                        && node.FlowId == automation.FlowId.Value
                        && node.Flow.HostId == hostId.Value
                        && node.Flow.IsEnabled
                        && node.DefinitionId == AutomationDefinitionIds.ManualSource.Value,
                    ct
                ),
            value =>
                ValueExistsAsync(
                    db,
                    hostId,
                    value,
                    (DashboardWidgetConfiguration.Value)configuration,
                    ct
                ),
            _ => Task.FromResult(true),
            poll =>
                db.TwitchPollTemplates.AnyAsync(
                    row => row.HostId == hostId.Value && row.Id == poll.TemplateId,
                    ct
                ),
            prediction =>
                db.TwitchPredictionTemplates.AnyAsync(
                    row => row.HostId == hostId.Value && row.Id == prediction.TemplateId,
                    ct
                ),
            _ => db.PointsSettings.AnyAsync(row => row.HostId == hostId.Value, ct),
            guessing =>
                db.Profiles.AnyAsync(
                    row => row.HostId == hostId.Value && row.Id == guessing.ProfileId,
                    ct
                )
        );
        return exists ? DashboardBindingAvailability.Ready : DashboardBindingAvailability.Missing;
    }

    private static Task<bool> ValueExistsAsync(
        BlokeBotDbContext db,
        DashboardHostId hostId,
        DashboardTarget.Value target,
        DashboardWidgetConfiguration.Value configuration,
        CancellationToken ct
    ) =>
        configuration.Selection.Match(
            _ =>
                db.CustomValueDefinitions.AnyAsync(
                    row =>
                        row.HostId == hostId.Value
                        && row.Id == target.DefinitionId
                        && row.Kind != CustomValueKind.Dictionary,
                    ct
                ),
            _ =>
                db.CustomValueDefinitions.AnyAsync(
                    row =>
                        row.HostId == hostId.Value
                        && row.Id == target.DefinitionId
                        && row.Kind == CustomValueKind.Dictionary,
                    ct
                )
        );
}
