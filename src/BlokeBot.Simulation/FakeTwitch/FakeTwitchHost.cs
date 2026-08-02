using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace BlokeBot.Simulation.FakeTwitch;

/// <summary>Hosts one fake Twitch authority on an internal loopback Kestrel listener.</summary>
internal sealed class FakeTwitchHost : IAsyncDisposable
{
    private FakeTwitchHost(WebApplication app, FakeTwitchAuthority authority, Uri origin)
    {
        App = app;
        Authority = authority;
        Origin = origin;
    }

    public WebApplication App { get; }

    public FakeTwitchAuthority Authority { get; }

    public Uri Origin { get; }

    public static async Task<FakeTwitchHost> StartAsync(
        FakeTwitchScenarioDefinition scenario,
        CancellationToken cancellationToken
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddFakeTwitch(scenario);
        var app = builder.Build();
        app.MapFakeTwitch();
        await app.StartAsync(cancellationToken);

        var addresses = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?.Addresses;
        var address = addresses?.SingleOrDefault(value =>
            value.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)
        );
        if (address is null)
        {
            await app.DisposeAsync();
            throw new InvalidOperationException(
                "The fake Twitch listener did not bind a loopback address."
            );
        }

        return new FakeTwitchHost(
            app,
            app.Services.GetRequiredService<FakeTwitchAuthority>(),
            new Uri(address.EndsWith('/') ? address : address + "/")
        );
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}
