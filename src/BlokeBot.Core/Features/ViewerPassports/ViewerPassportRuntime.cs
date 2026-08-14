namespace BlokeBot.Core.Features.ViewerPassports;

internal sealed class ViewerPassportRuntime(
    ViewerPassportService passports,
    TimeProvider clock,
    ILogger<ViewerPassportRuntime> log
) : IChatMessageObserver
{
    public async ValueTask MessageReceivedAsync(
        ChatMessage message,
        CancellationToken cancellationToken
    )
    {
        if (
            !message.Tags.TryGetValue("user-id", out var twitchUserId)
            || string.IsNullOrWhiteSpace(twitchUserId)
        )
        {
            return;
        }

        try
        {
            _ = await passports.RecordStreamAttendanceAsync(
                message.Channel,
                new(
                    twitchUserId,
                    message.Login,
                    message.Tags.GetValueOrDefault("display-name", message.Login)
                ),
                OccurredAtUtc(message),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(
                "Viewer passport stream attendance failed with {FailureType}.",
                exception.GetType().Name
            );
        }
    }

    private DateTimeOffset OccurredAtUtc(ChatMessage message) =>
        message.Tags.TryGetValue("tmi-sent-ts", out var timestamp)
        && long.TryParse(timestamp, out var unixMilliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
            : clock.GetUtcNow();
}
