using System.Globalization;
using System.Net;
using System.Text.Json;

namespace BlokeBot.Twitch;

public sealed record HelixAdSchedule(
    DateTimeOffset? NextAdAt,
    DateTimeOffset? LastAdAt,
    int DurationSeconds
);

public abstract record HelixAdScheduleOutcome
{
    private HelixAdScheduleOutcome() { }

    public sealed record Available(HelixAdSchedule Schedule) : HelixAdScheduleOutcome;

    public sealed record Unauthorized : HelixAdScheduleOutcome;

    public sealed record Unavailable : HelixAdScheduleOutcome;
}

public sealed partial class HelixClient
{
    public async Task<HelixAdScheduleOutcome> GetAdScheduleAsync(
        HelixRequestContext context,
        string broadcasterId,
        CancellationToken cancellation
    )
    {
        using var request = HelixRequest.Create(
            HttpMethod.Get,
            endpointPolicy
                .HelixEndpoint($"channels/ads?broadcaster_id={Uri.EscapeDataString(broadcasterId)}")
                .AbsoluteUri,
            context
        );
        using var response = await _http.SendAsync(request, cancellation);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new HelixAdScheduleOutcome.Unauthorized();
        }
        if (!response.IsSuccessStatusCode)
        {
            return new HelixAdScheduleOutcome.Unavailable();
        }
        try
        {
            using var payload = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellation),
                cancellationToken: cancellation
            );
            if (
                payload.RootElement.ValueKind != JsonValueKind.Object
                || !payload.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() != 1
            )
            {
                return new HelixAdScheduleOutcome.Unavailable();
            }
            var row = data[0];
            if (
                row.ValueKind != JsonValueKind.Object
                || !ReadOptionalTime(row, "next_ad_at", out var next)
                || !ReadOptionalTime(row, "last_ad_at", out var last)
                || !row.TryGetProperty("duration", out var duration)
            )
            {
                return new HelixAdScheduleOutcome.Unavailable();
            }
            var seconds =
                duration.ValueKind == JsonValueKind.Number && duration.TryGetInt32(out var n) ? n
                : duration.ValueKind == JsonValueKind.String
                && int.TryParse(
                    duration.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out n
                )
                    ? n
                : -1;
            return seconds is >= 0 and <= 3600
                ? new HelixAdScheduleOutcome.Available(new(next, last, seconds))
                : new HelixAdScheduleOutcome.Unavailable();
        }
        catch (JsonException)
        {
            return new HelixAdScheduleOutcome.Unavailable();
        }
    }

    private static bool ReadOptionalTime(JsonElement json, string name, out DateTimeOffset? time)
    {
        time = null;
        if (!json.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        var text = value.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }
        if (
            !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed
            )
        )
        {
            return false;
        }
        time = parsed.ToUniversalTime();
        return true;
    }
}
