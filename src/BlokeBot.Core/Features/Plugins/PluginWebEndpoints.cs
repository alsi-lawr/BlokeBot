using System.Collections.Immutable;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

internal static class PluginWebEndpoints
{
    internal static void MapPluginWebEndpoints(this WebApplication app)
    {
        _ = app.MapPost(
            "/plugins/{plugin}/hosts/{host:int}/features/{feature}/webhooks/{webhook}",
            InvokeWebhookAsync
        );
        _ = app.MapPost(
                "/plugins/{plugin}/hosts/{host:int}/features/{feature}/actions/{action}",
                InvokeActionAsync
            )
            .RequireAuthorization("Operator");
    }

    private static async Task<IResult> InvokeWebhookAsync(
        HttpContext http,
        string plugin,
        int host,
        string feature,
        string webhook,
        IPluginDispatchSnapshotProvider snapshots,
        IPluginDispatchInvoker invoker,
        CancellationToken cancellationToken
    )
    {
        if (
            !PluginId.TryCreate(plugin, out var pluginId)
            || !PluginHostId.TryCreate(host, out var hostId)
            || !PluginFeatureId.TryCreate(feature, out var featureId)
            || !PluginWebhookId.TryCreate(webhook, out var webhookId)
            || !snapshots.Current.Webhooks.TryGetValue(
                new(pluginId, featureId, hostId, webhookId),
                out var endpoint
            )
        )
        {
            return Results.NotFound();
        }

        var input = await ReadRequestAsync(http.Request, cancellationToken);
        if (input is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        var context = new PluginInvocationContext.Channel(
            endpoint.Declaration.Installation,
            hostId,
            Web: new(PluginWebInvocationKind.Webhook, webhookId.Value, http.Request.Method)
        );
        return MapWebhook(
            await invoker.InvokeWebhookAsync(endpoint, context, input, cancellationToken)
        );
    }

    private static async Task<IResult> InvokeActionAsync(
        HttpContext http,
        string plugin,
        int host,
        string feature,
        string action,
        IPluginDispatchSnapshotProvider snapshots,
        IPluginDispatchInvoker invoker,
        CancellationToken cancellationToken
    )
    {
        var session = AuthenticatedSession.FromPrincipal(http.User);
        var selectedHost = session.State.Match<int?>(
            static _ => null,
            static selected => selected.Selection.Current.Id,
            static _ => null
        );
        if (!session.HasCapability(AuthSessionCapability.Operator) || selectedHost != host)
        {
            return Results.Forbid();
        }
        if (
            !PluginId.TryCreate(plugin, out var pluginId)
            || !PluginHostId.TryCreate(host, out var hostId)
            || !PluginFeatureId.TryCreate(feature, out var featureId)
            || !PluginActionId.TryCreate(action, out var actionId)
            || !snapshots.Current.Actions.TryGetValue(
                new(pluginId, featureId, hostId, actionId),
                out var endpoint
            )
        )
        {
            return Results.NotFound();
        }

        var input = await ReadRequestAsync(http.Request, cancellationToken);
        if (input is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        var context = new PluginInvocationContext.Channel(
            endpoint.Declaration.Installation,
            hostId,
            new(
                session.Login,
                session.DisplayText,
                string.IsNullOrWhiteSpace(session.UserId) ? null : session.UserId,
                session.CurrentHostRoleIs(AuthRole.Streamer),
                session.CurrentHostRoleIs(AuthRole.Moderator),
                IsSubscriber: false
            ),
            Web: new(PluginWebInvocationKind.Action, actionId.Value, http.Request.Method)
        );
        return MapAction(
            await invoker.InvokeActionAsync(endpoint, context, input, cancellationToken)
        );
    }

    private static async ValueTask<PluginValue.Map?> ReadRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            request.ContentLength is > PluginContractLimits.MaximumWebRequestBodyBytes
            || request.Headers.Count > PluginContractLimits.MaximumHttpHeaders
        )
        {
            return null;
        }

        var headers = ImmutableArray.CreateBuilder<KeyValuePair<string, string>>();
        var headerBytes = 0;
        foreach (var header in request.Headers.OrderBy(static header => header.Key))
        {
            var value = header.Value.ToString();
            headerBytes += System.Text.Encoding.UTF8.GetByteCount(header.Key);
            headerBytes += System.Text.Encoding.UTF8.GetByteCount(value);
            if (headerBytes > PluginContractLimits.MaximumWebRequestHeaderBytes)
            {
                return null;
            }
            headers.Add(new(header.Key, value));
        }

        using var body = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (body.Length + read > PluginContractLimits.MaximumWebRequestBodyBytes)
            {
                return null;
            }
            body.Write(buffer, 0, read);
        }

        return PluginInvocationInputs.Web(request.Method, headers, body.ToArray());
    }

    private static IResult MapWebhook(PluginWebDispatchOutcome outcome) =>
        outcome switch
        {
            PluginWebDispatchOutcome.Returned returned => WebhookResponse(returned.Value),
            PluginWebDispatchOutcome.AuthenticationRejected => Results.Unauthorized(),
            PluginWebDispatchOutcome.Rejected or PluginWebDispatchOutcome.Stale =>
                Results.NotFound(),
            PluginWebDispatchOutcome.Cancelled => Results.StatusCode(
                StatusCodes.Status503ServiceUnavailable
            ),
            PluginWebDispatchOutcome.Failed => Results.StatusCode(StatusCodes.Status502BadGateway),
            _ => Results.StatusCode(StatusCodes.Status502BadGateway),
        };

    private static IResult WebhookResponse(PluginValue value)
    {
        if (value is not PluginValue.Map map)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        var fields = map.Properties.ToDictionary(
            static property => property.Name,
            static property => property.Value,
            StringComparer.Ordinal
        );
        return
            fields.TryGetValue("status", out var statusValue)
            && statusValue is PluginValue.Number statusNumber
            && statusNumber.Value is >= 200 and <= 599
            && statusNumber.Value == Math.Truncate(statusNumber.Value)
            && fields.TryGetValue("body", out var bodyValue)
            && bodyValue is PluginValue.String body
            ? Results.Text(body.Value, statusCode: (int)statusNumber.Value)
            : Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    private static IResult MapAction(PluginWebDispatchOutcome outcome) =>
        outcome switch
        {
            PluginWebDispatchOutcome.Returned returned => Results.Json(returned.Value),
            PluginWebDispatchOutcome.Rejected or PluginWebDispatchOutcome.Stale =>
                Results.NotFound(),
            PluginWebDispatchOutcome.Cancelled => Results.StatusCode(
                StatusCodes.Status503ServiceUnavailable
            ),
            PluginWebDispatchOutcome.Failed or PluginWebDispatchOutcome.AuthenticationRejected =>
                Results.StatusCode(StatusCodes.Status502BadGateway),
            _ => Results.StatusCode(StatusCodes.Status502BadGateway),
        };
}
