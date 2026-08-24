using System.Net;
using BlokeBot.Core.Features.HostedChannels.Runtime;

namespace BlokeBot.Simulation;

internal sealed class SimulationStartupCoordinator(
    SimulationFixtureSeeder fixtures,
    SimulationPluginFeatureScenario pluginFeatures
)
{
    public async Task BootstrapAsync(WebApplication app, CancellationToken cancellationToken)
    {
        var host = await fixtures.SeedAsync(cancellationToken);
        var dashboard = app.Urls.SingleOrDefault(url =>
            url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)
        );
        var dashboardOrigin =
            dashboard
            ?? throw new InvalidOperationException(
                "Simulation requires a loopback HTTP dashboard listener."
            );

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer(),
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(dashboardOrigin) };

        await FollowRedirectsAsync(client, "/auth/login?start=true&returnUrl=/", cancellationToken);
        await FollowRedirectsAsync(client, "/oauth/start", cancellationToken);
        await FollowRedirectsAsync(client, "/oauth/channel-bot/start", cancellationToken);
        await FollowRedirectsAsync(client, "/oauth/broadcaster/start", cancellationToken);
        _ = await app
            .Services.GetRequiredService<HostedChannelRuntimeControlService>()
            .Start(host.Id)
            .ExecuteAsync(cancellationToken);
        await pluginFeatures.SeedAsync(host.Id, cancellationToken);
    }

    private static async Task FollowRedirectsAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken
    )
    {
        Uri next = new(client.BaseAddress!, path);
        for (var redirects = 0; redirects < 8; redirects++)
        {
            using var response = await client.GetAsync(next, cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new HttpRequestException(
                        $"Simulation OAuth bootstrap failed at {next} with {(int)response.StatusCode}: {body}"
                    );
                }
                _ = response.EnsureSuccessStatusCode();
                return;
            }

            next = response.Headers.Location is { IsAbsoluteUri: true } location
                ? location
                : new Uri(next, response.Headers.Location!);
        }

        throw new InvalidOperationException(
            "Simulation OAuth bootstrap exceeded its redirect limit."
        );
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode
            is HttpStatusCode.Moved
                or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect;
}
