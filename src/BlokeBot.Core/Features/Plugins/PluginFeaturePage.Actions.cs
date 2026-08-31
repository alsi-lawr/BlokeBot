using BlokeBot.Core.Components.Layout;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Plugins;

public partial class PluginFeaturePage
{
    private async Task SubmitGeneratedActionAsync(PluginPageFormSubmission submission)
    {
        if (_session is null)
        {
            return;
        }
        _ = PluginPageMessageId.TryCreate(Guid.NewGuid(), out var messageId);
        var origin = new Uri(_navigation.BaseUri).GetLeftPart(UriPartial.Authority);
        var response = new PluginPageBridgeResponse(false, null, "The action is unavailable.");
        await RunSelectedHostMutationAsync(
            _session.Binding.Feature.HostId.Value,
            async () =>
                response = await ExecuteAsync(
                    new PluginPageBridgeRequest.Action(
                        messageId,
                        submission.Action,
                        submission.Input
                    ),
                    origin
                )
        );
        _feedback = new(
            response.Message,
            response.Accepted ? PageSaveFeedbackKind.Success : PageSaveFeedbackKind.Failure
        );
    }

    [JSInvokable]
    public async Task<PluginPageBridgeResponse> ReceiveEmbeddedMessageAsync(
        string origin,
        string json
    )
    {
        var session = _session;
        if (
            session is null
            || PluginPageBridgeProtocol.Parse(json, session.Id)
                is not PluginPageBridgeParseOutcome.Parsed parsed
        )
        {
            return Rejected("The plugin message was rejected.");
        }

        var response = Rejected("The selected channel is no longer authorized.");
        await RunSelectedHostMutationAsync(
            session.Binding.Feature.HostId.Value,
            async () => response = await ExecuteAsync(parsed.Request, origin)
        );
        return response;
    }

    private async Task<PluginPageBridgeResponse> ExecuteAsync(
        PluginPageBridgeRequest request,
        string origin
    )
    {
        var session = _session;
        return session is null || CurrentEndpoint(session.Binding) is not { } endpoint
                ? Rejected("This plugin page changed. Reload it and try again.")
            : (
                _sessions.AdmitMessage(
                    session.Id,
                    request.MessageId,
                    PluginPageSessionBinding.From(endpoint),
                    origin
                )
                is not PluginPageMessageAdmission.Admitted
            )
                ? Rejected("The plugin message was expired, replayed, or invalid.")
            : request is PluginPageBridgeRequest.Navigate navigation
                ? new(true, navigation.Url.AbsoluteUri, "Opening the requested page.")
            : await ExecuteActionAsync(
                endpoint,
                (PluginPageBridgeRequest.Action)request,
                session.Id
            );
    }

    private async Task<PluginPageBridgeResponse> ExecuteActionAsync(
        PluginPageEndpoint page,
        PluginPageBridgeRequest.Action request,
        PluginPageSessionId sessionId
    )
    {
        var key = new PluginActionRouteKey(
            page.State.Key.PluginId,
            page.State.Key.FeatureId,
            page.State.Key.HostId,
            request.ActionId
        );
        if (
            !_dispatch.Current.PageActions.TryGetValue(key, out var action)
            || action.Declaration.Installation != page.Definition.Declaration.Installation
            || action.State.Fence != page.State.Fence
            || action.State.Generation != page.State.Generation
        )
        {
            return Rejected("This plugin action is unavailable.");
        }
        if (
            PluginPageActionInputValidator.Validate(action.Descriptor, request.Input)
            is not PluginPageActionInputValidationOutcome.Accepted accepted
        )
        {
            return Rejected("The plugin action input is invalid.");
        }

        var context = new PluginInvocationContext.Page(
            page.Definition.Declaration.Installation,
            page.State.Key.HostId,
            page.Definition.Id,
            sessionId
        );
        var outcome = await _invoker.InvokePageActionAsync(
            action,
            context,
            accepted.Input,
            CancellationToken.None
        );
        return outcome switch
        {
            PluginDispatchInvocationOutcome.Returned => new(true, null, "Plugin action completed."),
            PluginDispatchInvocationOutcome.Cancelled => Rejected(
                "The plugin action was interrupted. Try again."
            ),
            PluginDispatchInvocationOutcome.Rejected or PluginDispatchInvocationOutcome.Stale =>
                Rejected("This plugin action changed. Reload the page and try again."),
            _ => Rejected("The plugin action failed."),
        };
    }

    private PluginPageEndpoint? CurrentEndpoint(PluginPageSessionBinding expected)
    {
        var resolution = _pages.Resolve(
            expected.Feature.PluginId,
            expected.Feature.FeatureId,
            expected.Feature.HostId,
            RouteValue
        );
        return
            resolution is PluginPageResolution.Available available
            && PluginPageSessionBinding.From(available.Endpoint) == expected
            && available.Endpoint.Definition.Id == expected.PageId
            ? available.Endpoint
            : null;
    }

    private static PluginPageBridgeResponse Rejected(string message) => new(false, null, message);
}
