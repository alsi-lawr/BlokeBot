namespace BlokeBot.Core.Features.Automations;

internal static class AutomationScenarioSimulation
{
    private static readonly IReadOnlyDictionary<string, Type> _effectContracts = new Dictionary<
        string,
        Type
    >(StringComparer.Ordinal)
    {
        ["start-countdown"] = typeof(CountdownActionConfiguration),
        ["reset-countdown"] = typeof(CountdownActionConfiguration),
        ["cancel-countdown"] = typeof(CountdownActionConfiguration),
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
                )
                || (
                    valid.Definition.Id == AutomationDefinitionIds.EndAutomationControl
                    && valid.Configuration is EndAutomationControlConfiguration
                ),
            AutomationNodeKind.Action => _effectContracts.TryGetValue(
                valid.Definition.Id.Value,
                out var type
            )
                && valid.Configuration.GetType() == type,
        };

    internal static AutomationNodeExecution Evaluate(
        AutomationConfiguration configuration,
        AutomationNativeActionPreparation preparation,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        AutomationContext context,
        AutomationScenarioEffectResult effectResult,
        DateTime now,
        Action<string>? preparedMessage = null
    ) =>
        configuration switch
        {
            EndAutomationControlConfiguration => AutomationNodeEvaluation.End(now),
            ConditionControlConfiguration => AutomationNodeEvaluation.Condition(inputs, now),
            DelayControlConfiguration delay => AutomationNodeEvaluation.Delay(delay, inputs, now),
            SendChatActionConfiguration => Prepared(
                AutomationNativeActionPreparation.Chat(inputs, context),
                effectResult,
                now,
                payload => preparedMessage?.Invoke(payload.Message)
            ),
            PlayOverlayCueActionConfiguration cue => Prepared(
                preparation.Cue(cue, fields, context),
                effectResult,
                now
            ),
            FulfilRedemptionActionConfiguration or CancelRedemptionActionConfiguration => Prepared(
                AutomationNativeActionPreparation.Redemption(context),
                effectResult,
                now
            ),
            SendShoutoutActionConfiguration => Prepared(
                AutomationNativeActionPreparation.Shoutout(context),
                effectResult,
                now
            ),
            StartPollActionConfiguration poll => Prepared(
                preparation.Poll(poll, fields, context),
                effectResult,
                now
            ),
            CreateMarkerActionConfiguration marker => Prepared(
                preparation.Marker(marker, fields, context),
                effectResult,
                now
            ),
            StartPredictionActionConfiguration prediction => Prepared(
                preparation.Prediction(prediction, fields, context),
                effectResult,
                now
            ),
            ResolvePredictionActionConfiguration resolve => Prepared(
                preparation.PredictionOutcome(resolve, fields, context),
                effectResult,
                now
            ),
            CountdownActionConfiguration countdown => AutomationNativeActionPreparation
                .Countdown(countdown, inputs, context.Actor, new(now, TimeSpan.Zero))
                .Match(
                    _ =>
                        countdown.Operation == CountdownOperation.Start
                            ? AutomationNodeEvaluation.Action(
                                effectResult switch
                                {
                                    AutomationScenarioEffectResult.Succeeded
                                    or AutomationScenarioEffectResult.Started =>
                                        new AutomationActionOutcome.CountdownStarted(),
                                    AutomationScenarioEffectResult.AlreadyRunning =>
                                        new AutomationActionOutcome.CountdownAlreadyRunning(),
                                    AutomationScenarioEffectResult.Failed =>
                                        new AutomationActionOutcome.Failed(
                                            "simulated-action-failed"
                                        ),
                                },
                                now,
                                "action-simulated"
                            )
                            : Effect(effectResult, now),
                    failed => new AutomationNodeExecution.Failed(failed.Code)
                ),
            EndPollActionConfiguration
            or CreateClipActionConfiguration
            or LockPredictionActionConfiguration
            or CancelPredictionActionConfiguration => Effect(effectResult, now),
            _ => new AutomationNodeExecution.Failed("simulation-unsupported"),
        };

    private static AutomationNodeExecution Prepared<T>(
        AutomationNativePreparation<T> prepared,
        AutomationScenarioEffectResult effectResult,
        DateTime now,
        Action<T>? capture = null
    ) =>
        prepared.Match(
            prepared =>
            {
                capture?.Invoke(prepared.Payload);
                return Effect(effectResult, now);
            },
            failed => new AutomationNodeExecution.Failed(failed.Code)
        );

    private static AutomationNodeExecution Effect(
        AutomationScenarioEffectResult effectResult,
        DateTime now
    ) =>
        AutomationNodeEvaluation.Action(
            effectResult == AutomationScenarioEffectResult.Failed
                ? new AutomationActionOutcome.Failed("simulated-action-failed")
                : new AutomationActionOutcome.Succeeded(),
            now,
            "action-simulated"
        );
}
