using System.Net.Http.Headers;
using System.Text.Json;
using BlokeBot.Auth.OAuth;
using BlokeBot.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace BlokeBot.Auth.Moderation;

internal sealed class ModeratedChannelLookupService(IHttpClientFactory httpClientFactory)
{
    private const string ModeratedChannelsEndpoint =
        "https://api.twitch.tv/helix/moderation/channels";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<string>> LoadModeratedLoginsAsync(
        WebAuthOptions options,
        string accessToken,
        string userId,
        string userLogin,
        CancellationToken ct
    )
    {
        var moderatedChannels = new List<ModeratedChannelData>();
        string? cursor = null;
        do
        {
            var query = new Dictionary<string, string?> { ["first"] = "100", ["user_id"] = userId };

            if (!string.IsNullOrWhiteSpace(cursor))
                query["after"] = cursor;

            var uri = QueryHelpers.AddQueryString(ModeratedChannelsEndpoint, query);
            using var request = CreateHelixRequest(uri, options, accessToken);
            using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<ModeratedChannelsResponse>(
                JsonOptions,
                ct
            );

            if (payload?.Data is { Length: > 0 } data)
                moderatedChannels.AddRange(data);

            cursor = payload?.Pagination.Cursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return moderatedChannels
            .Select(channel => LoginName.Parse(channel.BroadcasterLogin).Value)
            .Where(login =>
                login.Length > 0
                && !string.Equals(login, userLogin, StringComparison.OrdinalIgnoreCase)
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HttpRequestMessage CreateHelixRequest(
        string uri,
        WebAuthOptions options,
        string accessToken
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", options.ClientId);
        return request;
    }
}
