using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationActionOutcome
{
    private AutomationActionOutcome() { }

    internal sealed record Succeeded : AutomationActionOutcome;

    internal sealed record Failed(string Code) : AutomationActionOutcome;
}

public sealed class AutomationActionExecutor(
    HostFeatureService features,
    IPublicChatMessageSender chat,
    IOverlayCueAdmissionService overlayCues,
    AutomationExpressionService expressions
)
{
    internal async Task<AutomationActionOutcome> ExecuteAsync(
        AutomationHostId hostId,
        AutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return configuration switch
            {
                SendChatActionConfiguration sendChat => await SendChatAsync(
                    hostId,
                    sendChat,
                    fields,
                    context,
                    cancellationToken
                ),
                PlayOverlayCueActionConfiguration cue => await PlayCueAsync(
                    hostId,
                    cue,
                    fields,
                    context,
                    cancellationToken
                ),
                _ => new AutomationActionOutcome.Failed("action-unsupported"),
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new AutomationActionOutcome.Failed("action-failed");
        }
    }

    private async Task<AutomationActionOutcome> SendChatAsync(
        AutomationHostId hostId,
        SendChatActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.None, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        var message = fields.TryGetValue(new("message"), out var expression)
            ? expressions.Evaluate(expression, context)
            : expressions.Interpolate(configuration.Message, context);
        if (
            message is not AutomationExpressionResult.Value value
            || value.Result is not string text
        )
        {
            return new AutomationActionOutcome.Failed("action-expression-invalid");
        }

        if (value.UsesSensitiveValues)
        {
            return new AutomationActionOutcome.Failed("sensitive-output-blocked");
        }

        var outcome = await chat.SendAsync(
            context.Channel.Login,
            text,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        return outcome.Match<AutomationActionOutcome>(
            static _ => new AutomationActionOutcome.Succeeded(),
            static _ => new AutomationActionOutcome.Failed("chat-rejected")
        );
    }

    private async Task<AutomationActionOutcome> PlayCueAsync(
        AutomationHostId hostId,
        PlayOverlayCueActionConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsEnabledAsync(hostId, HostFeatureFlags.Overlays, cancellationToken))
        {
            return new AutomationActionOutcome.Failed("feature-disabled");
        }

        var targetId = configuration.TargetId.Value;
        var cueId = configuration.CueId.Value;
        if (
            !TryEvaluateGuid(fields, "target-id", targetId, context, out targetId)
            || !TryEvaluateGuid(fields, "cue-id", cueId, context, out cueId)
        )
        {
            return new AutomationActionOutcome.Failed("action-expression-invalid");
        }

        var references = await overlayCues.ResolveReferencesAsync(
            new(hostId.Value, targetId, cueId),
            cancellationToken
        );
        if (
            references is not OverlayCueReferenceOutcome.Available
            || !await IsEnabledAsync(hostId, HostFeatureFlags.Overlays, cancellationToken)
        )
        {
            return new AutomationActionOutcome.Failed("overlay-reference-unavailable");
        }

        var admissionCatalog = await overlayCues.QueryCatalogAsync(hostId.Value, cancellationToken);
        var cue = admissionCatalog.Cues.SingleOrDefault(value => value.Id == cueId);
        if (cue is null)
        {
            return new AutomationActionOutcome.Failed("overlay-reference-unavailable");
        }

        var admission = await overlayCues.AdmitAsync(
            new(
                hostId.Value,
                targetId,
                cueId,
                cue.DefaultQueuePolicy,
                OverlayCueAdmissionOrigin.Automation,
                OverlayCueSafeContext.Empty
            ),
            cancellationToken
        );
        return admission is OverlayCueAdmissionOutcome.Running or OverlayCueAdmissionOutcome.Queued
            ? new AutomationActionOutcome.Succeeded()
            : new AutomationActionOutcome.Failed("overlay-rejected");
    }

    private bool TryEvaluateGuid(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> fields,
        string fieldId,
        Guid configured,
        AutomationContext context,
        out Guid value
    )
    {
        value = configured;
        if (!fields.TryGetValue(new(fieldId), out var expression))
        {
            return true;
        }

        var evaluated = expressions.Evaluate(expression, context);
        return evaluated is AutomationExpressionResult.Value { Result: { } result }
            && Guid.TryParse(
                Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture),
                out value
            );
    }

    private async Task<bool> IsEnabledAsync(
        AutomationHostId hostId,
        HostFeatureFlags secondary,
        CancellationToken cancellationToken
    )
    {
        var enabled = await features.Load(hostId.Value).RunAsync(cancellationToken);
        return enabled.Match(
            flags =>
                flags.Contains(HostFeatureFlags.Automations)
                && (secondary == HostFeatureFlags.None || flags.Contains(secondary)),
            static () => false
        );
    }
}
