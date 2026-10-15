using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;

namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationNativePreparation<T>
{
    private AutomationNativePreparation() { }

    internal abstract TResult Match<TResult>(
        Func<Prepared, TResult> prepared,
        Func<Failed, TResult> failed
    );

    internal sealed record Prepared(T Payload) : AutomationNativePreparation<T>
    {
        internal override TResult Match<TResult>(
            Func<Prepared, TResult> prepared,
            Func<Failed, TResult> failed
        ) => prepared(this);
    }

    internal sealed record Failed(string Code) : AutomationNativePreparation<T>
    {
        internal override TResult Match<TResult>(
            Func<Prepared, TResult> prepared,
            Func<Failed, TResult> failed
        ) => failed(this);
    }
}

internal sealed record AutomationPreparedChat(string Channel, string Message);

internal sealed record AutomationPreparedCue(Guid TargetId, Guid CueId);

internal sealed record AutomationPreparedRedemption(string RedemptionId, string RewardId);

internal sealed class AutomationNativeActionPreparation(AutomationExpressionService expressions)
{
    internal static AutomationNativePreparation<AutomationPreparedChat> Chat(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        AutomationContext context
    ) =>
        !inputs.TryGetValue(new("message"), out var input)
        || AutomationPublicSinkAdmission.AdmitText(input)
            is not AutomationPublicTextAdmission.Admitted admitted
            ? new AutomationNativePreparation<AutomationPreparedChat>.Failed(
                "sensitive-output-blocked"
            )
        : string.IsNullOrWhiteSpace(context.Channel.Login)
        || string.IsNullOrWhiteSpace(admitted.Text)
            ? new AutomationNativePreparation<AutomationPreparedChat>.Failed("chat-rejected")
        : new AutomationNativePreparation<AutomationPreparedChat>.Prepared(
            new(context.Channel.Login, admitted.Text)
        );

    internal AutomationNativePreparation<AutomationPreparedCue> Cue(
        PlayOverlayCueActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context
    ) =>
        GuidField(fields, "target-id", configuration.TargetId.Value, context)
            .Match<AutomationNativePreparation<AutomationPreparedCue>>(
                target =>
                    GuidField(fields, "cue-id", configuration.CueId.Value, context)
                        .Match<AutomationNativePreparation<AutomationPreparedCue>>(
                            cue => new AutomationNativePreparation<AutomationPreparedCue>.Prepared(
                                new(target.Payload, cue.Payload)
                            ),
                            failed => new AutomationNativePreparation<AutomationPreparedCue>.Failed(
                                failed.Code
                            )
                        ),
                failed => new AutomationNativePreparation<AutomationPreparedCue>.Failed(failed.Code)
            );

    internal static AutomationNativePreparation<AutomationPreparedRedemption> Redemption(
        AutomationContext context
    ) =>
        context.Event.SourceDefinitionId == AutomationDefinitionIds.RewardRedemptionSource
        && TextVariable(context, "redemption_id") is { } redemptionId
        && TextVariable(context, "reward_id") is { } rewardId
            ? new AutomationNativePreparation<AutomationPreparedRedemption>.Prepared(
                new(redemptionId, rewardId)
            )
            : new AutomationNativePreparation<AutomationPreparedRedemption>.Failed(
                "redemption-context-missing"
            );

    internal static AutomationNativePreparation<string> Shoutout(AutomationContext context) =>
        context.Actor is { } actor && !string.IsNullOrWhiteSpace(actor.Login)
            ? new AutomationNativePreparation<string>.Prepared(actor.Login)
            : new AutomationNativePreparation<string>.Failed("shoutout-target-missing");

    internal AutomationNativePreparation<string> Marker(
        CreateMarkerActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context
    ) =>
        Text(fields, "description", configuration.Description, context)
            .Match<AutomationNativePreparation<string>>(
                text =>
                    NativeOperationAutomations.ValidateMarkerDescription(text.Payload).IsValid
                        ? text
                        : new AutomationNativePreparation<string>.Failed("marker-invalid"),
                failed => failed
            );

    internal AutomationNativePreparation<string> PredictionOutcome(
        ResolvePredictionActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context
    ) =>
        Text(fields, "winning-outcome-id", configuration.WinningOutcomeId, context)
            .Match<AutomationNativePreparation<string>>(
                text =>
                    NativeOperationAutomations.ValidateOutcomeIdentifier(text.Payload).IsValid
                        ? text
                        : new AutomationNativePreparation<string>.Failed(
                            "prediction-outcome-invalid"
                        ),
                failed => failed
            );

    internal AutomationNativePreparation<PollTemplateDraft> Poll(
        StartPollActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context
    ) =>
        Text(fields, "title", configuration.Title, context)
            .Match<AutomationNativePreparation<PollTemplateDraft>>(
                title =>
                    Lines(fields, "choices", configuration.Choices, context)
                        .Match<AutomationNativePreparation<PollTemplateDraft>>(
                            choices =>
                                new PollTemplateDraft(
                                    title.Payload,
                                    choices.Payload,
                                    configuration.DurationSeconds,
                                    configuration.ChannelPointsPerVote is not null,
                                    configuration.ChannelPointsPerVote
                                ).Validate()
                                    is PollTemplateValidationOutcome.Valid valid
                                    ? new AutomationNativePreparation<PollTemplateDraft>.Prepared(
                                        valid.Draft
                                    )
                                    : new AutomationNativePreparation<PollTemplateDraft>.Failed(
                                        "poll-invalid"
                                    ),
                            failed => new AutomationNativePreparation<PollTemplateDraft>.Failed(
                                failed.Code
                            )
                        ),
                failed => new AutomationNativePreparation<PollTemplateDraft>.Failed(failed.Code)
            );

    internal AutomationNativePreparation<PredictionTemplateDraft> Prediction(
        StartPredictionActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context
    ) =>
        Text(fields, "title", configuration.Title, context)
            .Match<AutomationNativePreparation<PredictionTemplateDraft>>(
                title =>
                    Lines(fields, "outcomes", configuration.Outcomes, context)
                        .Match<AutomationNativePreparation<PredictionTemplateDraft>>(
                            outcomes =>
                                new PredictionTemplateDraft(
                                    title.Payload,
                                    outcomes.Payload,
                                    configuration.WindowSeconds
                                ).Validate()
                                    is PredictionTemplateValidationOutcome.Valid valid
                                    ? new AutomationNativePreparation<PredictionTemplateDraft>.Prepared(
                                        valid.Draft
                                    )
                                    : new AutomationNativePreparation<PredictionTemplateDraft>.Failed(
                                        "prediction-invalid"
                                    ),
                            failed => new AutomationNativePreparation<PredictionTemplateDraft>.Failed(
                                failed.Code
                            )
                        ),
                failed => new AutomationNativePreparation<PredictionTemplateDraft>.Failed(
                    failed.Code
                )
            );

    private AutomationNativePreparation<string> Text(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        string fieldId,
        string template,
        AutomationContext context
    ) =>
        (
            fields.TryGetValue(new(fieldId), out var expression)
                ? expressions.Evaluate(expression, context)
                : expressions.Interpolate(template, context)
        ).Match<AutomationNativePreparation<string>>(
            value =>
                AutomationPublicSinkAdmission.AdmitText(value)
                    is AutomationPublicTextAdmission.Admitted admitted
                    ? new AutomationNativePreparation<string>.Prepared(admitted.Text)
                    : new AutomationNativePreparation<string>.Failed("sensitive-output-blocked"),
            _ => new AutomationNativePreparation<string>.Failed("action-expression-invalid")
        );

    private AutomationNativePreparation<string[]> Lines(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        string fieldId,
        string template,
        AutomationContext context
    )
    {
        if (fields.ContainsKey(new(fieldId)))
        {
            return Text(fields, fieldId, template, context)
                .Match<AutomationNativePreparation<string[]>>(
                    text => new AutomationNativePreparation<string[]>.Prepared([
                        .. NativeOperationAutomations.SplitEntries(text.Payload),
                    ]),
                    failed => new AutomationNativePreparation<string[]>.Failed(failed.Code)
                );
        }
        AutomationNativePreparation<string[]> result =
            new AutomationNativePreparation<string[]>.Prepared([]);
        foreach (var line in NativeOperationAutomations.SplitEntries(template))
        {
            result = result.Match<AutomationNativePreparation<string[]>>(
                lines =>
                    Text(fields, fieldId, line, context)
                        .Match<AutomationNativePreparation<string[]>>(
                            text => new AutomationNativePreparation<string[]>.Prepared([
                                .. lines.Payload,
                                text.Payload.Trim(),
                            ]),
                            failed => new AutomationNativePreparation<string[]>.Failed(failed.Code)
                        ),
                failed => failed
            );
            if (result is AutomationNativePreparation<string[]>.Failed)
            {
                return result;
            }
        }
        return result.Match<AutomationNativePreparation<string[]>>(
            lines => new AutomationNativePreparation<string[]>.Prepared([
                .. lines.Payload.Where(entry => entry.Length > 0),
            ]),
            failed => failed
        );
    }

    private AutomationNativePreparation<Guid> GuidField(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        string fieldId,
        Guid configured,
        AutomationContext context
    ) =>
        !fields.TryGetValue(new(fieldId), out var expression)
            ? new AutomationNativePreparation<Guid>.Prepared(configured)
            : expressions
                .Evaluate(expression, context)
                .Match<AutomationNativePreparation<Guid>>(
                    value =>
                        Guid.TryParse(
                            Convert.ToString(
                                value.Result,
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            out var parsed
                        )
                            ? new AutomationNativePreparation<Guid>.Prepared(parsed)
                            : new AutomationNativePreparation<Guid>.Failed(
                                "action-expression-invalid"
                            ),
                    _ => new AutomationNativePreparation<Guid>.Failed("action-expression-invalid")
                );

    private static string? TextVariable(AutomationContext context, string name) =>
        context.Variables.ForExecution().TryGetValue(new(name), out var variable)
        && variable.Value is AutomationValue.Text { Value: var text }
        && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}
