using Microsoft.AspNetCore.Mvc;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotApplication
{
    internal static void MapEventSubWebhookEndpoint(this WebApplication app) =>
        _ = app.MapPost(
                "/eventsub/twitch",
                async (
                    HttpRequest request,
                    IEventSubWebhookIngress ingress,
                    CancellationToken ct
                ) =>
                {
                    const int MaxBodyBytes = 512 * 1024;
                    if (request.ContentLength is > MaxBodyBytes)
                    {
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    }

                    await using var stream = new MemoryStream();
                    var buffer = new byte[16 * 1024];
                    var total = 0;
                    while (true)
                    {
                        var read = await request.Body.ReadAsync(buffer, ct);
                        if (read is 0)
                        {
                            break;
                        }

                        total += read;
                        if (total > MaxBodyBytes)
                        {
                            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                        }

                        await stream.WriteAsync(buffer.AsMemory(0, read), ct);
                    }

                    var result = await ingress.HandleAsync(
                        request.Headers["Twitch-Eventsub-Message-Id"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Message-Type"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Message-Timestamp"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Message-Signature"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Subscription-Type"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Subscription-Version"].FirstOrDefault(),
                        stream.ToArray(),
                        ct
                    );
                    return result.Challenge is null
                        ? Results.StatusCode(result.StatusCode)
                        : Results.Text(result.Challenge, "text/plain");
                }
            )
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithMetadata(new SkipStatusCodePagesAttribute());
}
