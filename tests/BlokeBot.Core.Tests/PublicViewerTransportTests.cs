using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.ViewerPortal.Boundary;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PublicViewerTransportTests
{
    [Test]
    public async Task IdleHandshakes_ConsumeTransportCapacityUntilTheActualConnectionCloses()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(App).Assembly.GetName().Name,
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory,
            }
        );
        _ = builder.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        _ = builder.AddBlokeBotCore(BlokeBotRuntimeMode.Offline);
        _ = builder.Services.RemoveAll<IHostedService>();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        _ = app.UseBlokeBotCore(BlokeBotRuntimeMode.Offline);
        await app.StartAsync();
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        var marker = app
            .Services.GetRequiredService<PublicDocumentProtector>()
            .Create(true, new ClaimsPrincipal())
            .Marker;
        var uri = new UriBuilder(address)
        {
            Scheme = "ws",
            Path = "/_BlAzOr",
            Query = $"DoCuMeNt={Uri.EscapeDataString(marker)}",
        }.Uri;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var first = new ClientWebSocket();
        using var second = new ClientWebSocket();
        using var denied = new ClientWebSocket();
        await first.ConnectAsync(uri, timeout.Token);
        await second.ConnectAsync(uri, timeout.Token);
        await denied.ConnectAsync(uri, timeout.Token);
        var terminal = false;
        try
        {
            var message = await denied.ReceiveAsync(new byte[32].AsMemory(), timeout.Token);
            terminal = message.MessageType == WebSocketMessageType.Close;
        }
        catch (WebSocketException)
        {
            terminal = true;
        }
        terminal.ShouldBeTrue();
        await HandshakeAsync(first, timeout.Token);
        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        using var replacement = new ClientWebSocket();
        await replacement.ConnectAsync(uri, timeout.Token);
        await HandshakeAsync(replacement, timeout.Token);
        await HandshakeAsync(second, timeout.Token);
    }

    private static async Task HandshakeAsync(ClientWebSocket socket, CancellationToken ct)
    {
        await socket.SendAsync(
            Encoding.UTF8.GetBytes("{\"protocol\":\"blazorpack\",\"version\":1}\u001e").AsMemory(),
            WebSocketMessageType.Text,
            true,
            ct
        );
        var buffer = new byte[256];
        var response = await socket.ReceiveAsync(buffer.AsMemory(), ct);
        Encoding.UTF8.GetString(buffer.AsSpan(0, response.Count)).ShouldBe("{}\u001e");
    }
}
