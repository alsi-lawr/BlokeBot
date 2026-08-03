using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Simulation.Tests;

public sealed class AuthenticatedRoutesPrerenderingTests
{
    [Test]
    public async Task InitialHtml_PrerendersRoutesOnlyForAnonymousRequests()
    {
        await using var simulation = await SimulationApplication.BuildAsync(
            ["--urls=http://127.0.0.1:0"],
            CancellationToken.None
        );
        await simulation.App.InitializeSimulationAsync(CancellationToken.None);
        await simulation.App.StartAsync();
        var address = simulation
            .App.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.ShouldHaveSingleItem();
        using var anonymous = new HttpClient { BaseAddress = new Uri(address) };
        using var authenticated = new HttpClient(
            new HttpClientHandler { CookieContainer = new CookieContainer() }
        )
        {
            BaseAddress = new Uri(address),
        };

        using var login = await authenticated.GetAsync(
            "/simulation/login?view=points-settings&theme=light"
        );
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        login.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/points/settings");

        var authenticatedHtml = await login.Content.ReadAsStringAsync();
        var authenticatedBody = Body(authenticatedHtml);
        authenticatedBody.ShouldContain("<!--Blazor:{\"type\":\"server\"");
        authenticatedBody.ShouldNotContain("\"prerenderId\"");
        authenticatedBody.ShouldNotContain("app-shell");
        authenticatedBody.ShouldNotContain("<h1");
        authenticatedHtml.ShouldNotContain("<title>BlokeBot | Points Settings</title>");

        foreach (
            var (path, title, heading) in new[]
            {
                (
                    "/points/leaderboard/samplechannel",
                    "BlokeBot | Points Leaderboard",
                    "Points leaderboard"
                ),
                (
                    "/guessing/leaderboard/samplechannel",
                    "BlokeBot | Guessing Leaderboard",
                    "Guessing leaderboard"
                ),
                ("/moments/samplechannel", "BlokeBot | Moment recap", "Weekly recap"),
            }
        )
        {
            using var response = await anonymous.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var html = await response.Content.ReadAsStringAsync();
            var body = Body(html);
            body.ShouldContain("<!--Blazor:{\"type\":\"server\",\"prerenderId\"");
            body.ShouldContain("app-shell");
            html.ShouldContain($"<title>{title}</title>");
            body.ShouldContain($"<h1");
            body.ShouldContain($">{heading}</h1>");
        }

        using var loginPage = await anonymous.GetAsync("/auth/login");
        loginPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        loginHtml.ShouldContain("<title>Sign in to BlokeBot</title>");
        loginHtml.ShouldContain(">Sign in to BlokeBot</h1>");
        loginHtml.ShouldNotContain("<!--Blazor:");
    }

    private static string Body(string html) =>
        html[html.IndexOf("<body", StringComparison.Ordinal)..];
}
