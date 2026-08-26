using System.Net;
using System.Net.Http.Headers;

namespace BlokeBot.Core.Features.Plugins;

internal abstract record PluginMarketplaceCatalogDownload
{
    private PluginMarketplaceCatalogDownload() { }

    internal sealed record Delivered(
        ReadOnlyMemory<byte> Content,
        string? SourceETag,
        DateTimeOffset? SourceModifiedAt
    ) : PluginMarketplaceCatalogDownload;

    internal sealed record NotModified(string? SourceETag, DateTimeOffset? SourceModifiedAt)
        : PluginMarketplaceCatalogDownload;

    internal sealed record Failed : PluginMarketplaceCatalogDownload;
}

internal interface IPluginMarketplaceCatalogTransport
{
    ValueTask<PluginMarketplaceCatalogDownload> DownloadAsync(
        string? entityTag,
        DateTimeOffset? modifiedSince,
        CancellationToken cancellationToken
    );
}

internal sealed class GitHubPluginMarketplaceCatalogTransport(IHttpClientFactory clients)
    : IPluginMarketplaceCatalogTransport
{
    internal const string ClientName = "BlokeBot.PluginMarketplace.Catalog";
    internal static readonly Uri CatalogUrl = new(
        "https://raw.githubusercontent.com/alsi-lawr/blokebot-plugins/master/catalog.json"
    );

    public async ValueTask<PluginMarketplaceCatalogDownload> DownloadAsync(
        string? entityTag,
        DateTimeOffset? modifiedSince,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, CatalogUrl);
            if (
                entityTag is not null
                && EntityTagHeaderValue.TryParse(entityTag, out var parsedEntityTag)
            )
            {
                request.Headers.IfNoneMatch.Add(parsedEntityTag);
            }
            request.Headers.IfModifiedSince = modifiedSince;
            using var response = await clients
                .CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return entityTag is null && modifiedSince is null
                    ? new PluginMarketplaceCatalogDownload.Failed()
                    : new PluginMarketplaceCatalogDownload.NotModified(
                        response.Headers.ETag?.ToString(),
                        response.Content.Headers.LastModified
                    );
            }

            if (!response.IsSuccessStatusCode)
            {
                return new PluginMarketplaceCatalogDownload.Failed();
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var target = new MemoryStream();
            await source.CopyToAsync(target, cancellationToken);
            return new PluginMarketplaceCatalogDownload.Delivered(
                target.ToArray(),
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return new PluginMarketplaceCatalogDownload.Failed();
        }
    }
}
