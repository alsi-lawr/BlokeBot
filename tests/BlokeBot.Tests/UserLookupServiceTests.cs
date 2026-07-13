using System.Net;
using System.Text;
using BlokeBot.Auth.OAuth;
using BlokeBot.Auth.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class UserLookupServiceTests
{
    [Test]
    public async Task HelixUserFoundByLogin_MappingBoundary_MapsEveryIdentityField()
    {
        var service = CreateService(
            """
            {
              "data": [
                {
                  "id": "user-id",
                  "login": "viewer",
                  "display_name": "Viewer",
                  "profile_image_url": "https://cdn.example/viewer.png"
                }
              ]
            }
            """
        );

        var result = await service.FindByLoginAsync("Viewer", CancellationToken.None);

        var identity = result.Match(
            user => user,
            () => throw new InvalidOperationException("Expected a mapped Twitch user identity.")
        );
        identity.Id.ShouldBe("user-id");
        identity.Login.ShouldBe("viewer");
        identity.DisplayName.ShouldBe("Viewer");
        identity.ProfileImageUrl.ShouldBe("https://cdn.example/viewer.png");
    }

    [Test]
    public async Task NoHelixUserFoundByLogin_MappingBoundary_ReturnsNone()
    {
        var service = CreateService("""{"data":[]}""");

        var result = await service.FindByLoginAsync("missing", CancellationToken.None);

        result.Match(_ => false, () => true).ShouldBeTrue();
    }

    [Test]
    public async Task HelixUserFoundByLoginWithoutId_MappingBoundary_ReturnsNone()
    {
        var service = CreateService(
            """
            {"data":[{"login":"viewer","display_name":"Viewer","profile_image_url":""}]}
            """
        );

        var result = await service.FindByLoginAsync("viewer", CancellationToken.None);

        result.Match(_ => false, () => true).ShouldBeTrue();
    }

    [Test]
    public async Task HelixUserFoundByLoginWithBlankLogin_MappingBoundary_ReturnsNone()
    {
        var service = CreateService(
            """
            {"data":[{"id":"user-id","login":" ","display_name":"Viewer","profile_image_url":""}]}
            """
        );

        var result = await service.FindByLoginAsync("viewer", CancellationToken.None);

        result.Match(_ => false, () => true).ShouldBeTrue();
    }

    [Test]
    public async Task ValidCurrentHelixUser_MappingBoundary_MapsEveryIdentityField()
    {
        var service = CreateService(
            """
            {
              "data": [
                {
                  "id": "current-id",
                  "login": "current",
                  "display_name": "Current User",
                  "profile_image_url": "https://cdn.example/current.png"
                }
              ]
            }
            """
        );

        var result = await service.GetCurrentUserAsync(
            new WebAuthOptions { ClientId = "client-id" },
            "access-token",
            CancellationToken.None
        );

        var identity = result.Match(
            user => user,
            () => throw new InvalidOperationException("Expected a mapped Twitch user identity.")
        );
        identity.Id.ShouldBe("current-id");
        identity.Login.ShouldBe("current");
        identity.DisplayName.ShouldBe("Current User");
        identity.ProfileImageUrl.ShouldBe("https://cdn.example/current.png");
    }

    [Test]
    public async Task CurrentHelixUserWithoutId_MappingBoundary_ReturnsNone()
    {
        var service = CreateService(
            """
            {
              "data": [
                {
                  "login": "current",
                  "display_name": "Current User",
                  "profile_image_url": "https://cdn.example/current.png"
                }
              ]
            }
            """
        );

        var result = await service.GetCurrentUserAsync(
            new WebAuthOptions { ClientId = "client-id" },
            "access-token",
            CancellationToken.None
        );

        result.Match(_ => false, () => true).ShouldBeTrue();
    }

    [Test]
    public async Task CurrentHelixUserWithBlankLogin_MappingBoundary_ReturnsNone()
    {
        var service = CreateService(
            """
            {
              "data": [
                {
                  "id": "current-id",
                  "login": " ",
                  "display_name": "Current User",
                  "profile_image_url": "https://cdn.example/current.png"
                }
              ]
            }
            """
        );

        var result = await service.GetCurrentUserAsync(
            new WebAuthOptions { ClientId = "client-id" },
            "access-token",
            CancellationToken.None
        );

        result.Match(_ => false, () => true).ShouldBeTrue();
    }

    [Test]
    public async Task HelixLookupFailure_MappingBoundary_PreservesHttpException()
    {
        var service = CreateService("""{"data":[]}""", HttpStatusCode.BadGateway);

        await Should.ThrowAsync<HttpRequestException>(() =>
            service.FindByLoginAsync("viewer", CancellationToken.None)
        );
    }

    [Test]
    public async Task CancelledLookup_MappingBoundary_PreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService("""{"data":[]}""");

        await Should.ThrowAsync<OperationCanceledException>(() =>
            service.FindByLoginAsync("viewer", cancellation.Token)
        );
    }

    private static UserLookupService CreateService(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK
    )
    {
        var configuration = new WebAuthConfiguration(
            Options.Create(new WebAuthOptions { ClientId = "client-id" }),
            new ConfigurationBuilder().Build()
        );
        return new UserLookupService(
            configuration,
            new StaticAccessTokenProvider("access-token"),
            new HelixClient(new JsonHttpClientFactory(response, statusCode))
        );
    }

    private sealed class StaticAccessTokenProvider(string accessToken) : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(accessToken);
        }
    }

    private sealed class JsonHttpClientFactory(string response, HttpStatusCode statusCode)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new JsonHttpMessageHandler(response, statusCode));
        }
    }

    private sealed class JsonHttpMessageHandler(string response, HttpStatusCode statusCode)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(response, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
