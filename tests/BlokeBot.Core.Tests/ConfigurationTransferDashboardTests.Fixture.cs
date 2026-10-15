using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferDashboardTests
{
    private static ConfigurationTransferCoordinator Coordinator(
        IDbContextFactory<BlokeBotDbContext> database,
        CustomStoredValueChanges? changes = null,
        IConfigurationImportObserverDispatcher? observers = null,
        IModeratorAuthorityService? authority = null
    )
    {
        var components = ConfigurationTransferAutomationTestServices.Create(database);
        var scenarios = new AutomationScenarioService(
            database,
            components.Catalog,
            components.Flows,
            TimeProvider.System,
            new AutomationExpressionService(),
            BotSettings.FromOptions(new BotOptions()),
            OAuthAuthorizationScopeSet.Create(["channel:bot"])
        );
        var automations = new AutomationConfigurationTransferAdapter(
            components.Flows,
            components.Catalog,
            TimeProvider.System,
            scenarios
        );
        return new(
            database,
            new(new(database, null!, TimeProvider.System), new(), TimeProvider.System),
            authority ?? new Granted(),
            new(),
            TimeProvider.System,
            NullLogger<ConfigurationTransferCoordinator>.Instance,
            TestAutomationUiChanges.Create(),
            changes ?? StoredValueTestChanges.Create(),
            new(database, UnavailableOverlayConfigurationTransferAdapter.Instance, automations),
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            automations,
            observers ?? UnavailableConfigurationImportObserverDispatcher.Instance,
            new(1, 1)
        );
    }

    private static ConfigurationDocumentExporter Exporter(
        IDbContextFactory<BlokeBotDbContext> database
    )
    {
        var components = ConfigurationTransferAutomationTestServices.Create(database);
        return new(
            database,
            new(),
            components.Catalog,
            components.Flows,
            TimeProvider.System,
            new(
                database,
                components.Catalog,
                components.Flows,
                TimeProvider.System,
                new AutomationExpressionService(),
                BotSettings.FromOptions(new BotOptions()),
                OAuthAuthorizationScopeSet.Create(["channel:bot"])
            )
        );
    }

    private static AuthenticatedSession Session(int hostId, AuthRole role = AuthRole.Streamer)
    {
        var selected = new BotHostChoice(hostId, "streamer", "Streamer", role);
        return new()
        {
            IsAuthenticated = true,
            UserId = "owner",
            Login = "streamer",
            State = new AuthSessionState.Selected(new BotHostSelection(selected, [selected])),
        };
    }

    private static ConfigurationImportSelection Selection(
        int hostId,
        ImportConflictStrategy strategy,
        params ConfigurationSectionId[] sections
    ) =>
        new(
            hostId,
            sections.Select(section => new SectionImportSelection(section, strategy, [])).ToArray(),
            new HashSet<HostFeatureFlags>()
        );

    private static ConfigurationDocumentV2 Document(
        DashboardsSectionV1 dashboards,
        AutomationsSectionV2? automations = null,
        CustomCommandsSectionV1? commands = null,
        GuessingSectionV1? guessing = null
    ) =>
        new(
            ConfigurationDocumentCodec.Format,
            ConfigurationDocumentCodec.CurrentVersion,
            DateTimeOffset.UtcNow,
            new("source", null),
            new(
                CustomCommands: commands,
                Automations: automations,
                Guessing: guessing,
                Dashboards: dashboards
            )
        );

    private static DashboardLayoutV1 Layout(params DashboardConfigurationV1[] configurations) =>
        new(
            "layout",
            "Operator",
            configurations
                .Select(
                    (configuration, index) =>
                        new DashboardWidgetV1($"widget-{index}", $"Widget {index}", configuration)
                )
                .ToArray(),
            new(
                12,
                56,
                configurations
                    .Select(
                        (_, index) =>
                            new DashboardWidgetPlacementV1(
                                $"widget-{index}",
                                new(index % 3 * 4, index / 3 * 3, 4, 3, index)
                            )
                    )
                    .ToArray()
            ),
            null
        );

    private static async Task<int> HostAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        string login
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            TwitchUserId = login + "-id",
            DisplayName = login,
            EnabledFeatures =
                HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Guessing,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<(Guid Flow, Guid[] Nodes)> FlowAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        int hostId,
        string name,
        params string?[] aliases
    )
    {
        var components = ConfigurationTransferAutomationTestServices.Create(database);
        var nodes = aliases
            .Select(
                (alias, index) =>
                    new AutomationFlowDraftNode(
                        new(Guid.NewGuid()),
                        new(
                            AutomationDefinitionIds.ManualSource.Value,
                            1,
                            JsonSerializer.SerializeToElement(
                                new ManualSourceConfiguration("identical"),
                                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                            )
                        ),
                        new(1),
                        AutomationNodeFailurePolicy.Stop,
                        ImmutableDictionary<
                            AutomationConfigurationFieldId,
                            AutomationInputBinding
                        >.Empty,
                        new(new(index + 1), new(0)),
                        alias
                    )
            )
            .ToImmutableArray();
        var saved = (
            await components.Flows.SaveAsync(
                new(null, new(hostId), name, 1, true, nodes, []),
                default
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        return (saved.FlowId.Value, nodes.Select(node => node.Id.Value).ToArray());
    }

    private static async Task<ChannelDashboard[]> DashboardsAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        int hostId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        return await db
            .ChannelDashboards.AsNoTracking()
            .Where(row => row.HostId == hostId)
            .OrderBy(row => row.Name)
            .ToArrayAsync();
    }

    private sealed class Granted : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int hostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }

    private sealed class AuditBoundary : SaveChangesInterceptor
    {
        internal bool Active { get; set; }
        internal CancellationTokenSource? CancelAtAudit { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data,
            InterceptionResult<int> result,
            CancellationToken ct = default
        )
        {
            if (!data.Context!.ChangeTracker.Entries<ConfigurationImportAudit>().Any())
            {
                return ValueTask.FromResult(result);
            }
            if (CancelAtAudit is { } cancellation)
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled<InterceptionResult<int>>(ct);
            }
            return Active
                ? ValueTask.FromException<InterceptionResult<int>>(
                    new IOException("Owned audit boundary failure")
                )
                : ValueTask.FromResult(result);
        }
    }
}
