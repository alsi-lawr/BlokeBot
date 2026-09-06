using System.Diagnostics.Metrics;
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
        using var admission = new TransportObservation();
        await using var app = builder.Build();
        admission.Attach(app.Services);
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
        Exception? failure = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var first = new ClientWebSocket();
            using var second = new ClientWebSocket();
            using var denied = new ClientWebSocket();
            await first.ConnectAsync(uri, timeout.Token);
            await second.ConnectAsync(uri, timeout.Token);
            await admission.TwoIdleTransports.WaitAsync(timeout.Token);
            var terminal = false;
            try
            {
                await denied.ConnectAsync(uri, timeout.Token);
                var message = await denied.ReceiveAsync(new byte[32].AsMemory(), timeout.Token);
                terminal = message.MessageType == WebSocketMessageType.Close;
            }
            catch (WebSocketException) when (!timeout.IsCancellationRequested)
            {
                terminal = true;
            }
            terminal.ShouldBeTrue();
            admission.CapacityRejections.ShouldBe(1);
            admission.OtherRejections.ShouldBe(0);
            admission.Transports.ShouldBe(2);
            await HandshakeAsync(first, timeout.Token);
            await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            using var replacement = new ClientWebSocket();
            await replacement.ConnectAsync(uri, timeout.Token);
            await HandshakeAsync(replacement, timeout.Token);
            await HandshakeAsync(second, timeout.Token);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await app.StopAsync(shutdown.Token);
            }
            catch (Exception cleanupFailure) when (failure is not null)
            {
                throw new AggregateException(failure, cleanupFailure);
            }
        }
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

    private sealed class TransportObservation : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly AsyncLocal<bool> _attaching = new();
        private readonly TaskCompletionSource _twoIdleTransports = new();
        private long _transports;
        private long _capacityRejections;
        private long _otherRejections;

        internal Task TwoIdleTransports => _twoIdleTransports.Task;
        internal long Transports => Interlocked.Read(ref _transports);
        internal long CapacityRejections => Interlocked.Read(ref _capacityRejections);
        internal long OtherRejections => Interlocked.Read(ref _otherRejections);

        internal TransportObservation()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (_attaching.Value && instrument.Meter.Name == "BlokeBot.PublicViewer")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) =>
                {
                    string? kind = null;
                    string? unit = null;
                    string? reason = null;
                    foreach (var tag in tags)
                    {
                        switch (tag.Key)
                        {
                            case "kind":
                                kind = tag.Value as string;
                                break;
                            case "unit":
                                unit = tag.Value as string;
                                break;
                            case "reason":
                                reason = tag.Value as string;
                                break;
                        }
                    }
                    if (instrument.Name == "public_viewer.leases" && kind == "Transport")
                    {
                        if (Interlocked.Add(ref _transports, value) == 2)
                        {
                            _ = _twoIdleTransports.TrySetResult();
                        }
                    }
                    else if (instrument.Name == "public_viewer.rejections")
                    {
                        if (unit == "Transport" && reason == "capacity")
                        {
                            _ = Interlocked.Add(ref _capacityRejections, value);
                        }
                        else
                        {
                            _ = Interlocked.Add(ref _otherRejections, value);
                        }
                    }
                }
            );
            _listener.Start();
        }

        internal void Attach(IServiceProvider services)
        {
            // Capture only this server's meter, not meters published by concurrent tests.
            _attaching.Value = true;
            try
            {
                _ = services.GetRequiredService<PublicViewerAdmission>();
            }
            finally
            {
                _attaching.Value = false;
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
