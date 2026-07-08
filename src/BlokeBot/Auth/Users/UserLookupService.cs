using System.Net.Http.Headers;
using System.Text.Json;
using Alsi.TwitchBot;
using BlokeBot.Auth.OAuth;
using BlokeBot.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace BlokeBot.Auth.Users;

internal sealed class UserLookupService(
    IHttpClientFactory httpClientFactory,
    WebAuthConfiguration configuration,
    ITwitchAccessTokenProvider tokens
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UserData?> FindByLoginAsync(string login, CancellationToken cancellationToken)
    {
        return await FindByLoginAsync(
            CreateCurrentOptions(),
            await tokens.GetAccessTokenAsync(cancellationToken),
            login,
            cancellationToken
        );
    }

    public async Task<UserData?> FindByLoginAsync(
        WebAuthOptions options,
        string accessToken,
        string login,
        CancellationToken cancellationToken
    )
    {
        var normalized = LoginName.Parse(login);
        if (normalized.IsEmpty)
            return null;

        var uri = QueryHelpers.AddQueryString(
            "https://api.twitch.tv/helix/users",
            new Dictionary<string, string?> { ["login"] = normalized.Value }
        );
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", options.ClientId);

        using var response = await httpClientFactory
            .CreateClient()
            .SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<UserResponse>(
            JsonOptions,
            cancellationToken
        );
        return payload?.Data.FirstOrDefault();
    }

    public async Task<UserData?> GetCurrentUserAsync(
        WebAuthOptions options,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        using var request = CreateRequest(
            "https://api.twitch.tv/helix/users",
            options,
            accessToken
        );
        using var response = await httpClientFactory
            .CreateClient()
            .SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<UserResponse>(
            JsonOptions,
            cancellationToken
        );
        var user = payload?.Data.FirstOrDefault();

        return string.IsNullOrWhiteSpace(user?.Id) || string.IsNullOrWhiteSpace(user.Login)
            ? null
            : user;
    }

    private static HttpRequestMessage CreateRequest(
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

    private WebAuthOptions CreateCurrentOptions() => configuration.CurrentOptions;
}
