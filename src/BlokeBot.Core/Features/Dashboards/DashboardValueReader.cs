using System.Data.Common;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardValueReader(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    DashboardAuthority authority,
    CustomStoredValueService values,
    ICustomCommandViewerResolver viewers,
    CustomStoredValueChanges changes,
    ILogger<DashboardValueReader> log
)
{
    internal DashboardValueSession CreateSession(Func<ValueTask> changed) =>
        new(this, changes, changed);

    internal async Task<DashboardValueState> ReadAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Value configuration,
        CustomCommandViewer? viewer,
        CustomValueDefinitionDraft? currentDefinition,
        Action<CustomValueTarget> bindTarget,
        CancellationToken ct
    )
    {
        try
        {
            if (
                await authority.AuthorizeAsync(session, host, ct)
                is DashboardResult<DashboardHostId>.Rejected rejected
            )
            {
                return new DashboardValueState.Rejected(rejected.Reason);
            }
            if (
                configuration.Binding
                is not DashboardBinding.Configured { Target: DashboardTarget.Value target }
            )
            {
                return new DashboardValueState.Unavailable(
                    DashboardValueUnavailableReason.Unconfigured
                );
            }
            var definition = currentDefinition;
            if (definition is null)
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var features = await db
                    .Hosts.Where(x => x.Id == host.Value)
                    .Select(x => (HostFeatureFlags?)x.EnabledFeatures)
                    .SingleOrDefaultAsync(ct);
                if (features is null)
                {
                    return new DashboardValueState.Unavailable(
                        DashboardValueUnavailableReason.Missing
                    );
                }
                if ((features.Value & HostFeatureFlags.CustomCommands) == 0)
                {
                    return new DashboardValueState.Unavailable(
                        DashboardValueUnavailableReason.FeatureDisabled
                    );
                }
                definition = await values.DefinitionAsync(host.Value, target.DefinitionId, ct);
            }
            if (definition is null)
            {
                return new DashboardValueState.Unavailable(DashboardValueUnavailableReason.Missing);
            }
            var key = configuration.Selection.Match<string?>(
                _ => definition.Kind == CustomValueKind.Dictionary ? null : string.Empty,
                entry =>
                    definition.Kind == CustomValueKind.Dictionary
                    && CustomValueIdentity.Valid(entry.Key)
                        ? entry.Key
                        : null
            );
            if (key is null)
            {
                return new DashboardValueState.Unavailable(
                    DashboardValueUnavailableReason.InvalidSelection
                );
            }
            if (
                definition.Scope == CustomValueScope.User
                && (viewer is null || !CustomValueIdentity.Valid(viewer.TwitchUserId))
            )
            {
                return new DashboardValueState.Unavailable(
                    DashboardValueUnavailableReason.ViewerRequired
                );
            }
            var identity = new CustomValueTarget(
                definition.Id,
                definition.Scope == CustomValueScope.Global ? string.Empty : viewer!.TwitchUserId,
                key
            );
            bindTarget(identity);
            var snapshot = await values.ReadValueAsync(host.Value, identity, ct);
            return snapshot is null
                    ? new DashboardValueState.Unavailable(DashboardValueUnavailableReason.Missing)
                : (snapshot.Features & HostFeatureFlags.CustomCommands) == 0
                    ? new DashboardValueState.Unavailable(
                        DashboardValueUnavailableReason.FeatureDisabled
                    )
                : new DashboardValueState.Available(
                    snapshot.Definition,
                    snapshot.Value,
                    snapshot.Definition.Scope == CustomValueScope.Global ? null : viewer
                );
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            log.LogWarning(
                exception,
                "Dashboard value read unavailable for host {HostId}.",
                host.Value
            );
            return new DashboardValueState.Unavailable(
                DashboardValueUnavailableReason.StorageUnavailable
            );
        }
    }

    internal async Task<CustomCommandViewerResolution> ResolveViewerAsync(
        string login,
        CancellationToken ct
    )
    {
        try
        {
            return await viewers.ResolveAsync(login, ct);
        }
        catch (HttpRequestException exception)
        {
            log.LogWarning(exception, "Dashboard viewer lookup unavailable.");
            return new CustomCommandViewerResolution.Unavailable();
        }
    }
}
