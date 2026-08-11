using BlokeBot.Core.Features.Automations;

namespace BlokeBot.Core.Features.Competitions;

public static class CompetitionAutomationDefinitionIds
{
    public static AutomationDefinitionId LifecycleSource { get; } = new("competition-lifecycle");
}

public sealed record CompetitionLifecycleSourceConfiguration : AutomationConfiguration;

internal sealed class CompetitionAutomationCatalogModule : IAutomationCatalogModule
{
    private static readonly AutomationSchemaCompatibility _schema = new(new(1), new(1));

    public AutomationModuleId Id { get; } = new("blokebot.competitions");

    public IEnumerable<IAutomationDefinition> Definitions { get; } = [LifecycleSource()];

    private static AutomationDefinition<CompetitionLifecycleSourceConfiguration> LifecycleSource() =>
        new(
            new(
                CompetitionAutomationDefinitionIds.LifecycleSource,
                AutomationNodeKind.Source,
                AutomationDefinitionScope.Host,
                _schema,
                new(
                    "Competition lifecycle",
                    "Starts an automation after a committed public competition lifecycle event.",
                    "Community progression"
                ),
                [],
                [
                    new(
                        new("flow"),
                        "Flow",
                        "Starts the connected automation.",
                        AutomationPortValueType.Flow
                    ),
                    new(
                        new("event-kind"),
                        "Event kind",
                        "The public competition lifecycle event kind.",
                        AutomationPortValueType.Text
                    ),
                    new(
                        new("competition-id"),
                        "Competition ID",
                        "The public competition identity.",
                        AutomationPortValueType.Text
                    ),
                    new(
                        new("public-payload"),
                        "Public payload",
                        "The bounded public lifecycle JSON payload.",
                        AutomationPortValueType.Text
                    ),
                    new(
                        new("channel"),
                        "Channel",
                        "The channel that owns the competition.",
                        AutomationPortValueType.Channel
                    ),
                    new(
                        new("event-time"),
                        "Event time",
                        "When the lifecycle event was committed.",
                        AutomationPortValueType.Timestamp
                    ),
                ],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            static _ => new AutomationConfigurationParseResult.Parsed(
                new CompetitionLifecycleSourceConfiguration()
            ),
            static _ => AutomationValidationResult.Valid
        );
}
