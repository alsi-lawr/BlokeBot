using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Simulation.Tests;

public sealed class EventSubWebhookHostTests
{
    [Test]
    public async Task UnauthenticatedFailure_PreservesProviderStatus()
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
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/eventsub/twitch")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Location.ShouldBeNull();
    }
}
