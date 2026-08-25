using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public enum PluginHttpMethod
{
    Get,
    Head,
    Post,
    Put,
    Patch,
    Delete,
}

public sealed record PluginHttpRequest(
    PluginHttpMethod Method,
    Uri Uri,
    ImmutableDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body
);

public enum PluginHttpRejectionCode
{
    InvalidUri,
    MethodNotAllowed,
    HeadersTooLarge,
    RequestBodyTooLarge,
    ConcurrencyLimitReached,
}

public enum PluginHttpFailureCode
{
    TimedOut,
    RedirectLimitExceeded,
    InvalidRedirect,
    ResponseTooLarge,
    TransportFailed,
}

public abstract record PluginHttpOutcome
{
    private PluginHttpOutcome() { }

    public sealed record Response(
        int StatusCode,
        ImmutableDictionary<string, string> Headers,
        ReadOnlyMemory<byte> Body
    ) : PluginHttpOutcome;

    public sealed record Rejected(PluginHttpRejectionCode Code) : PluginHttpOutcome;

    public sealed record Failed(PluginHttpFailureCode Code) : PluginHttpOutcome;
}

public sealed partial class PluginOutboundHttpClient
{
    public const string ClientName = "BlokeBot.PluginOutbound";

    private readonly ConcurrentDictionary<PluginId, SemaphoreSlim> _concurrency = new();
    private readonly IHttpClientFactory _clients;
    private readonly TimeSpan _maximumDuration;

    public PluginOutboundHttpClient(IHttpClientFactory clients)
        : this(
            clients,
            TimeSpan.FromMilliseconds(PluginContractLimits.MaximumHttpDurationMilliseconds)
        ) { }

    internal PluginOutboundHttpClient(IHttpClientFactory clients, TimeSpan maximumDuration)
    {
        ArgumentNullException.ThrowIfNull(clients);
        if (maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }
        _clients = clients;
        _maximumDuration = maximumDuration;
    }

    public async ValueTask<PluginHttpOutcome> SendAsync(
        PluginId pluginId,
        PluginHttpRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!ValidUri(request.Uri))
        {
            return new PluginHttpOutcome.Rejected(PluginHttpRejectionCode.InvalidUri);
        }
        if (!Enum.IsDefined(request.Method))
        {
            return new PluginHttpOutcome.Rejected(PluginHttpRejectionCode.MethodNotAllowed);
        }
        if (!ValidHeaders(request.Headers))
        {
            return new PluginHttpOutcome.Rejected(PluginHttpRejectionCode.HeadersTooLarge);
        }
        if (request.Body.Length > PluginContractLimits.MaximumHttpRequestBodyBytes)
        {
            return new PluginHttpOutcome.Rejected(PluginHttpRejectionCode.RequestBodyTooLarge);
        }

        var gate = _concurrency.GetOrAdd(
            pluginId,
            static _ => new(PluginContractLimits.MaximumConcurrentHttpRequestsPerPlugin)
        );
        if (!await gate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return new PluginHttpOutcome.Rejected(PluginHttpRejectionCode.ConcurrencyLimitReached);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_maximumDuration);
            try
            {
                return await SendCoreAsync(request, timeout.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                return new PluginHttpOutcome.Failed(PluginHttpFailureCode.TimedOut);
            }
            catch (HttpRequestException)
            {
                return new PluginHttpOutcome.Failed(PluginHttpFailureCode.TransportFailed);
            }
        }
        finally
        {
            _ = gate.Release();
        }
    }

    private async ValueTask<PluginHttpOutcome> SendCoreAsync(
        PluginHttpRequest request,
        CancellationToken cancellationToken
    )
    {
        var client = _clients.CreateClient(ClientName);
        var uri = request.Uri;
        var method = request.Method;
        for (var redirect = 0; ; redirect++)
        {
            using var message = Message(method, uri, request.Headers, request.Body);
            using var response = await client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (!IsRedirect(response.StatusCode))
            {
                var body = await ReadBoundedAsync(response.Content, cancellationToken);
                return body is null
                    ? new PluginHttpOutcome.Failed(PluginHttpFailureCode.ResponseTooLarge)
                    : new PluginHttpOutcome.Response(
                        (int)response.StatusCode,
                        ResponseHeaders(response),
                        body.Value
                    );
            }

            if (redirect >= PluginContractLimits.MaximumHttpRedirects)
            {
                return new PluginHttpOutcome.Failed(PluginHttpFailureCode.RedirectLimitExceeded);
            }
            if (
                response.Headers.Location is not { } location
                || !Uri.TryCreate(uri, location, out var next)
                || !ValidUri(next)
            )
            {
                return new PluginHttpOutcome.Failed(PluginHttpFailureCode.InvalidRedirect);
            }

            uri = next;
            if (response.StatusCode == HttpStatusCode.SeeOther)
            {
                method = PluginHttpMethod.Get;
            }
        }
    }

    private static HttpRequestMessage Message(
        PluginHttpMethod method,
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        ReadOnlyMemory<byte> body
    )
    {
        var message = new HttpRequestMessage(Method(method), uri);
        if (body.Length > 0 && method is not PluginHttpMethod.Get and not PluginHttpMethod.Head)
        {
            message.Content = new ByteArrayContent(body.ToArray());
        }
        foreach (var header in headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                _ = message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        return message;
    }

    private static HttpMethod Method(PluginHttpMethod method) =>
        method switch
        {
            PluginHttpMethod.Get => HttpMethod.Get,
            PluginHttpMethod.Head => HttpMethod.Head,
            PluginHttpMethod.Post => HttpMethod.Post,
            PluginHttpMethod.Put => HttpMethod.Put,
            PluginHttpMethod.Patch => HttpMethod.Patch,
            PluginHttpMethod.Delete => HttpMethod.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

    private static bool IsRedirect(HttpStatusCode status) =>
        status
            is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect;
}
