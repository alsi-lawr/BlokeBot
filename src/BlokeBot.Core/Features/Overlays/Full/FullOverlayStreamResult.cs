using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed class FullOverlayStreamResult(
    FullOverlayDelivery delivery,
    FullOverlayRenderLease lease,
    ILogger<FullOverlayStreamResult> logger
) : IResult
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(HttpContext context)
    {
        var response = context.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-store, private, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        try
        {
            while (!context.RequestAborted.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(
                    context.RequestAborted
                );
                heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    if (!await lease.Refresh.WaitToReadAsync(heartbeat.Token))
                    {
                        break;
                    }
                    while (lease.Refresh.TryRead(out _)) { }
                    var frame = await delivery.ProjectAsync(lease, context.RequestAborted);
                    if (frame is null)
                    {
                        if (!await delivery.RefreshAuthorityAsync(lease, context.RequestAborted))
                        {
                            break;
                        }
                        // A draft revision changed during projection. Retry the same selected
                        // publication and connection, rather than restarting production owners.
                        lease.Wake();
                        continue;
                    }
                    await response.WriteAsync(
                        $"data: {JsonSerializer.Serialize(frame, _json)}\n\n",
                        context.RequestAborted
                    );
                }
                catch (OperationCanceledException)
                    when (!context.RequestAborted.IsCancellationRequested)
                {
                    if (!await delivery.RefreshAuthorityAsync(lease, context.RequestAborted))
                    {
                        break;
                    }
                    await response.WriteAsync(": keepalive\n\n", context.RequestAborted);
                    lease.Wake();
                }
                await response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Full overlay transport ended with {FailureClassification} ({FailureType}).",
                "transport-unavailable",
                exception.GetType().FullName
            );
        }
        finally
        {
            delivery.Close(lease);
        }
    }
}
