using System.Collections.Immutable;
using System.Net;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Features.Tests;

public sealed class PluginOutboundHttpTests
{
    [Test]
    public async Task ResponseAndRedirects_ReturnBoundedTypedResults()
    {
        var handler = new ScriptedHandler(
            request =>
            {
                request.RequestUri!.AbsolutePath.ShouldBe("/start");
                var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                response.Headers.Location = new Uri("/complete", UriKind.Relative);
                return response;
            },
            request =>
            {
                request.Method.ShouldBe(HttpMethod.Post);
                request.RequestUri!.AbsolutePath.ShouldBe("/complete");
                request.Headers.GetValues("x-plugin").ShouldBe(["value"]);
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent("accepted"),
                };
            }
        );
        var client = Client(handler);

        var outcome = await client.SendAsync(
            Plugin(),
            Request(
                PluginHttpMethod.Post,
                headers: ImmutableDictionary<string, string>.Empty.Add("x-plugin", "value"),
                body: "payload"u8.ToArray()
            ),
            CancellationToken.None
        );

        var response = outcome.ShouldBeOfType<PluginHttpOutcome.Response>();
        response.StatusCode.ShouldBe(202);
        System.Text.Encoding.UTF8.GetString(response.Body.Span).ShouldBe("accepted");
        handler.Calls.ShouldBe(2);
    }

    [Test]
    public async Task RedirectAndResponseLimits_ReturnTypedFailures()
    {
        var redirects = Client(
            new ScriptedHandler(
                Enumerable
                    .Range(0, PluginContractLimits.MaximumHttpRedirects + 1)
                    .Select<int, Func<HttpRequestMessage, HttpResponseMessage>>(index =>
                        request =>
                        {
                            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                            response.Headers.Location = new Uri(
                                $"/redirect-{index}",
                                UriKind.Relative
                            );
                            return response;
                        }
                    )
                    .ToArray()
            )
        );
        var redirectFailure = await redirects.SendAsync(
            Plugin(),
            Request(PluginHttpMethod.Get),
            CancellationToken.None
        );
        redirectFailure
            .ShouldBeOfType<PluginHttpOutcome.Failed>()
            .Code.ShouldBe(PluginHttpFailureCode.RedirectLimitExceeded);

        var oversized = Client(
            new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(
                    new byte[PluginContractLimits.MaximumHttpResponseBodyBytes + 1]
                ),
            })
        );
        var sizeFailure = await oversized.SendAsync(
            Plugin(),
            Request(PluginHttpMethod.Get),
            CancellationToken.None
        );
        sizeFailure
            .ShouldBeOfType<PluginHttpOutcome.Failed>()
            .Code.ShouldBe(PluginHttpFailureCode.ResponseTooLarge);
    }

    [Test]
    public async Task TimeoutAndCallerCancellation_RemainDistinct()
    {
        var timeoutHandler = new WaitingHandler();
        var timed = new PluginOutboundHttpClient(
            new Factory(timeoutHandler),
            TimeSpan.FromMilliseconds(30)
        );
        var timeout = await timed.SendAsync(
            Plugin(),
            Request(PluginHttpMethod.Get),
            CancellationToken.None
        );
        timeout
            .ShouldBeOfType<PluginHttpOutcome.Failed>()
            .Code.ShouldBe(PluginHttpFailureCode.TimedOut);
        timeoutHandler.CancellationObserved.ShouldBeTrue();

        var cancellationHandler = new WaitingHandler();
        var cancellable = Client(cancellationHandler);
        using var cancellation = new CancellationTokenSource();
        var pending = cancellable
            .SendAsync(Plugin(), Request(PluginHttpMethod.Get), cancellation.Token)
            .AsTask();
        await cancellationHandler.Started.Task;
        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(pending);
        cancellationHandler.CancellationObserved.ShouldBeTrue();
    }

    [Test]
    public async Task PerPluginConcurrencyLimit_RejectsOnlyTheExcessRequest()
    {
        var handler = new BlockingHandler(
            PluginContractLimits.MaximumConcurrentHttpRequestsPerPlugin
        );
        var client = Client(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var active = Enumerable
            .Range(0, PluginContractLimits.MaximumConcurrentHttpRequestsPerPlugin)
            .Select(_ =>
                client
                    .SendAsync(Plugin(), Request(PluginHttpMethod.Get), cancellation.Token)
                    .AsTask()
            )
            .ToArray();
        await handler.AllStarted.Task.WaitAsync(cancellation.Token);

        var excess = await client.SendAsync(
            Plugin(),
            Request(PluginHttpMethod.Get),
            cancellation.Token
        );

        excess
            .ShouldBeOfType<PluginHttpOutcome.Rejected>()
            .Code.ShouldBe(PluginHttpRejectionCode.ConcurrencyLimitReached);
        handler.Release();
        foreach (var response in await Task.WhenAll(active))
        {
            _ = response.ShouldBeOfType<PluginHttpOutcome.Response>();
        }
    }

    private static PluginOutboundHttpClient Client(HttpMessageHandler handler) =>
        new(new Factory(handler));

    private static PluginHttpRequest Request(
        PluginHttpMethod method,
        ImmutableDictionary<string, string>? headers = null,
        byte[]? body = null
    ) =>
        new(
            method,
            new Uri("https://plugin.test/start"),
            headers ?? ImmutableDictionary<string, string>.Empty,
            body ?? []
        );

    private static PluginId Plugin() => PluginContractFixtures.PluginId("community.http");

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ScriptedHandler(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses
    ) : HttpMessageHandler
    {
        private int _calls;

        internal int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var index = Interlocked.Increment(ref _calls) - 1;
            return Task.FromResult(responses[index](request));
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool CancellationObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            _ = Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Infinite wait completed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class BlockingHandler(int expected) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _started;

        internal TaskCompletionSource AllStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (Interlocked.Increment(ref _started) == expected)
            {
                _ = AllStarted.TrySetResult();
            }
            await _release.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("ok") };
        }
    }
}
