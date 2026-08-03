using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class EventSubClient(
    IHttpClientFactory httpClientFactory,
    TwitchEndpointPolicy endpointPolicy,
    EventSubWebhookOptions webhook,
    IAppAccessTokenProvider appAccessTokens,
    IEventSubSubscriptionVerification verification
) : IEventSubSubscriptionTransport
{
    private const int _maxErrorBodyBytes = 16 * 1024;
    private const int _maxDiagnosticFieldLength = 256;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public async Task<string> CreateAsync(
        string clientId,
        EventSubSubscriptionRequest subscription,
        CancellationToken cancellationToken
    )
    {
        var payload = new CreateSubscriptionRequest
        {
            Type = subscription.Type,
            Version = subscription.Version,
            Condition = subscription.Condition,
            Transport = new SubscriptionTransport
            {
                Method = "webhook",
                Callback = webhook.CallbackUri.AbsoluteUri,
                Secret = webhook.Secret,
            },
        };
        var managementContext = await ManagementContextAsync(clientId, cancellationToken);
        using var request = HelixRequest.Create(
            HttpMethod.Post,
            endpointPolicy.HelixEndpoint("eventsub/subscriptions").AbsoluteUri,
            managementContext
        );
        request.Content = JsonContent.Create(payload, options: _jsonOptions);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateSubscriptionFailureAsync(
                response,
                cancellationToken,
                [
                    managementContext.ClientId,
                    managementContext.AccessToken,
                    webhook.CallbackUri.AbsoluteUri,
                    webhook.Secret,
                    .. subscription.Condition.Values,
                ]
            );
        }

        var result = await response.Content.ReadFromJsonAsync<SubscriptionResponse>(
            _jsonOptions,
            cancellationToken
        );
        var subscriptionId =
            result?.Data.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException(
                "Twitch did not return an EventSub subscription ID."
            );
        await verification.WaitAsync(subscriptionId, cancellationToken);
        return subscriptionId;
    }

    private async Task<HelixRequestContext> ManagementContextAsync(
        string clientId,
        CancellationToken cancellationToken
    ) => new(clientId, await appAccessTokens.GetAccessTokenAsync(cancellationToken));

    private static async ValueTask<EventSubSubscriptionCreationException> CreateSubscriptionFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        IReadOnlyList<string> sensitiveValues
    )
    {
        var body = await ReadBoundedBodyAsync(response, cancellationToken);
        string? providerError = null;
        string? providerMessage = null;
        string? existingSubscriptionId = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind is JsonValueKind.Object)
            {
                providerError = ReadDiagnosticText(document.RootElement, "error", sensitiveValues);
                providerMessage = ReadDiagnosticText(
                    document.RootElement,
                    "message",
                    sensitiveValues
                );
                existingSubscriptionId = ReadDiagnosticIdentifier(
                    document.RootElement,
                    sensitiveValues,
                    "existing_subscription_id",
                    "subscription_id",
                    "id"
                );
            }
        }
        catch (JsonException)
        {
            // Twitch occasionally returns a non-JSON error body. The bounded status remains useful.
        }

        return new EventSubSubscriptionCreationException(
            response.StatusCode,
            providerError,
            providerMessage,
            existingSubscriptionId
        );
    }

    private static async ValueTask<string> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[_maxErrorBodyBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(length, buffer.Length - length),
                cancellationToken
            );
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static string? ReadDiagnosticText(
        JsonElement root,
        string propertyName,
        IReadOnlyList<string> sensitiveValues
    )
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        var value = property.ValueKind is JsonValueKind.String ? property.GetString() : null;
        return SanitizeDiagnosticText(value, sensitiveValues);
    }

    private static string? ReadDiagnosticIdentifier(
        JsonElement root,
        IReadOnlyList<string> sensitiveValues,
        params string[] propertyNames
    )
    {
        foreach (var propertyName in propertyNames)
        {
            if (
                root.TryGetProperty(propertyName, out var property)
                && property.ValueKind is JsonValueKind.String
            )
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return SanitizeDiagnosticIdentifier(value, sensitiveValues);
                }
            }
        }

        return null;
    }

    private static string? SanitizeDiagnosticText(
        string? value,
        IReadOnlyList<string> sensitiveValues
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (
            var sensitiveValue in sensitiveValues.Where(static value =>
                !string.IsNullOrEmpty(value)
            )
        )
        {
            value = value.Replace(sensitiveValue, "[redacted]", StringComparison.Ordinal);
        }

        if (
            value.Contains("access_token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || value.Contains("client_secret", StringComparison.OrdinalIgnoreCase)
            || value.Contains("cookie", StringComparison.OrdinalIgnoreCase)
            || value.Contains("bearer ", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "[redacted]";
        }

        var sanitized = new string(
            value.Where(static character => !char.IsControl(character)).ToArray()
        );
        return sanitized.Trim() is { Length: > 0 } trimmed
            ? trimmed[..Math.Min(trimmed.Length, _maxDiagnosticFieldLength)]
            : null;
    }

    private static string? SanitizeDiagnosticIdentifier(
        string value,
        IReadOnlyList<string> sensitiveValues
    )
    {
        if (
            sensitiveValues.Any(sensitiveValue =>
                !string.IsNullOrEmpty(sensitiveValue)
                && value.Contains(sensitiveValue, StringComparison.Ordinal)
            )
        )
        {
            return null;
        }

        var sanitized = new string(
            value
                .Where(static character =>
                    char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                )
                .ToArray()
        );
        return sanitized.Length is 0
            ? null
            : sanitized[..Math.Min(sanitized.Length, _maxDiagnosticFieldLength)];
    }

    public async Task ResetAsync(string clientId, CancellationToken cancellationToken)
    {
        var context = await ManagementContextAsync(clientId, cancellationToken);
        foreach (var subscription in await ListOwnedAsync(context, cancellationToken))
        {
            await DeleteAsync(context, subscription.Id, cancellationToken);
        }
    }

    public async Task<IReadOnlySet<string>> ListEnabledOwnedIdsAsync(
        string clientId,
        CancellationToken cancellationToken
    ) =>
        (
            await ListOwnedAsync(
                await ManagementContextAsync(clientId, cancellationToken),
                cancellationToken
            )
        )
            .Where(static subscription =>
                subscription.Status.Equals("enabled", StringComparison.Ordinal)
            )
            .Select(static subscription => subscription.Id)
            .ToHashSet(StringComparer.Ordinal);

    private async Task<IReadOnlyList<EventSubSubscription>> ListOwnedAsync(
        HelixRequestContext context,
        CancellationToken cancellationToken
    )
    {
        var owned = new List<EventSubSubscription>();
        string? cursor = null;
        do
        {
            var inventory = await ListPageAsync(context, cursor, cancellationToken);
            owned.AddRange(
                inventory.Subscriptions.Where(subscription =>
                    subscription.Method.Equals("webhook", StringComparison.Ordinal)
                    && string.Equals(
                        subscription.Callback,
                        webhook.CallbackUri.AbsoluteUri,
                        StringComparison.Ordinal
                    )
                )
            );
            cursor = inventory.Cursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return owned;
    }

    public async Task DeleteAsync(
        string clientId,
        string subscriptionId,
        CancellationToken cancellationToken
    ) =>
        await DeleteAsync(
            await ManagementContextAsync(clientId, cancellationToken),
            subscriptionId,
            cancellationToken
        );

    private async Task DeleteAsync(
        HelixRequestContext context,
        string subscriptionId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            endpointPolicy.HelixEndpoint("eventsub/subscriptions").AbsoluteUri
            + $"?id={Uri.EscapeDataString(subscriptionId)}";
        using var request = HelixRequest.Create(HttpMethod.Delete, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return;
        }

        _ = response.EnsureSuccessStatusCode();
    }

    public async Task<EventSubSubscriptionInventory> ListSubscriptionsAsync(
        string clientId,
        string? after,
        CancellationToken cancellationToken
    ) =>
        await ListPageAsync(
            await ManagementContextAsync(clientId, cancellationToken),
            after,
            cancellationToken
        );

    private async Task<EventSubSubscriptionInventory> ListPageAsync(
        HelixRequestContext context,
        string? after,
        CancellationToken cancellationToken
    )
    {
        var uri = endpointPolicy.HelixEndpoint("eventsub/subscriptions").AbsoluteUri;
        if (!string.IsNullOrWhiteSpace(after))
        {
            uri += $"?after={Uri.EscapeDataString(after)}";
        }

        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        _ = response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SubscriptionListResponse>(
            _jsonOptions,
            cancellationToken
        );
        return new EventSubSubscriptionInventory(
            result?.Data.Select(static item => item.ToSubscription()).ToArray() ?? [],
            result?.Pagination?.Cursor
        );
    }

    private sealed record CreateSubscriptionRequest
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("condition")]
        public required IReadOnlyDictionary<string, string> Condition { get; init; }

        [JsonPropertyName("transport")]
        public required SubscriptionTransport Transport { get; init; }
    }

    private sealed record SubscriptionTransport
    {
        [JsonPropertyName("method")]
        public string Method { get; init; } = string.Empty;

        [JsonPropertyName("callback")]
        public string? Callback { get; init; }

        [JsonPropertyName("secret")]
        public string Secret { get; init; } = string.Empty;
    }

    private sealed record SubscriptionResponse
    {
        [JsonPropertyName("data")]
        public required ImmutableArray<SubscriptionItem> Data { get; init; }
    }

    private sealed record SubscriptionListResponse
    {
        [JsonPropertyName("data")]
        public ImmutableArray<SubscriptionItem> Data { get; init; }

        [JsonPropertyName("pagination")]
        public Pagination? Pagination { get; init; }
    }

    private sealed record Pagination
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; init; }
    }

    private sealed record SubscriptionItem
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("condition")]
        public IReadOnlyDictionary<string, string> Condition { get; init; } =
            new Dictionary<string, string>();

        [JsonPropertyName("transport")]
        public SubscriptionTransport Transport { get; init; } = new();

        public EventSubSubscription ToSubscription() =>
            new(Id, Status, Type, Version, Condition, Transport.Method, Transport.Callback);
    }
}

public sealed record EventSubSubscriptionInventory(
    IReadOnlyList<EventSubSubscription> Subscriptions,
    string? Cursor
);

public sealed record EventSubSubscription(
    string Id,
    string Status,
    string Type,
    string Version,
    IReadOnlyDictionary<string, string> Condition,
    string Method,
    string? Callback
);
