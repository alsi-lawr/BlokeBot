namespace BlokeBot.Core.Features.Automations;

internal static class AutomationScenarioSimulation
{
    private static readonly IReadOnlyDictionary<string, Type> _effectContracts = new Dictionary<
        string,
        Type
    >(StringComparer.Ordinal)
    {
        ["send-chat"] = typeof(SendChatActionConfiguration),
        ["play-overlay-cue"] = typeof(PlayOverlayCueActionConfiguration),
        ["fulfil-redemption"] = typeof(FulfilRedemptionActionConfiguration),
        ["cancel-redemption"] = typeof(CancelRedemptionActionConfiguration),
        ["send-shoutout"] = typeof(SendShoutoutActionConfiguration),
        ["start-poll"] = typeof(StartPollActionConfiguration),
        ["end-poll"] = typeof(EndPollActionConfiguration),
        ["create-clip"] = typeof(CreateClipActionConfiguration),
        ["create-marker"] = typeof(CreateMarkerActionConfiguration),
        ["start-prediction"] = typeof(StartPredictionActionConfiguration),
        ["lock-prediction"] = typeof(LockPredictionActionConfiguration),
        ["cancel-prediction"] = typeof(CancelPredictionActionConfiguration),
        ["resolve-prediction"] = typeof(ResolvePredictionActionConfiguration),
    };

    internal static bool Supports(
        AutomationConfigurationCheck.Valid valid,
        AutomationDataResolver data
    ) =>
        valid.Definition.PluginProvenance is null
        && valid.Configuration is not PluginAutomationConfiguration
        && valid.Definition.Kind switch
        {
            AutomationNodeKind.Source => true,
            AutomationNodeKind.Value or AutomationNodeKind.Transform => data.SupportsScenarios(
                valid.Definition.Id
            ),
            AutomationNodeKind.Control => (
                valid.Definition.Id.Value
                    is AutomationSubflowDefinitions.Entry
                        or AutomationSubflowDefinitions.Exit
                        or AutomationSubflowDefinitions.Invoke
                && valid.Configuration is AutomationSubflowConfiguration
            )
                || (
                    valid.Definition.Id.Value == "condition"
                    && valid.Configuration is ConditionControlConfiguration
                )
                || (
                    valid.Definition.Id.Value == "delay"
                    && valid.Configuration is DelayControlConfiguration
                ),
            AutomationNodeKind.Action => _effectContracts.TryGetValue(
                valid.Definition.Id.Value,
                out var type
            )
                && valid.Configuration.GetType() == type,
        };

    internal static AutomationNodeExecution Evaluate(
        AutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        AutomationScenarioEffectResult effectResult,
        DateTime now
    ) =>
        configuration switch
        {
            ConditionControlConfiguration => AutomationNodeEvaluation.Condition(inputs, now),
            DelayControlConfiguration delay => AutomationNodeEvaluation.Delay(delay, now),
            SendChatActionConfiguration
                when !inputs.TryGetValue(new("message"), out var message)
                    || AutomationPublicSinkAdmission.AdmitText(message)
                        is not AutomationPublicTextAdmission.Admitted =>
                new AutomationNodeExecution.Failed("sensitive-output-blocked"),
            _ => effectResult == AutomationScenarioEffectResult.Failed
                ? new AutomationNodeExecution.Failed("simulated-action-failed")
                : new AutomationNodeExecution.Succeeded("action-simulated", "complete", now),
        };
}
