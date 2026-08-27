using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

internal abstract record PluginMarketplaceRepositoryDownload
{
    private PluginMarketplaceRepositoryDownload() { }

    internal sealed record Delivered(
        PluginMarketplaceRepositorySnapshot Repository,
        string? SourceETag,
        DateTimeOffset? SourceModifiedAt
    ) : PluginMarketplaceRepositoryDownload;

    internal sealed record NotModified(string? SourceETag, DateTimeOffset? SourceModifiedAt)
        : PluginMarketplaceRepositoryDownload;

    internal sealed record Failed : PluginMarketplaceRepositoryDownload;
}

internal interface IPluginMarketplaceRepositoryTransport
{
    ValueTask<PluginMarketplaceRepositoryDownload> DownloadAsync(
        string? entityTag,
        DateTimeOffset? modifiedSince,
        CancellationToken cancellationToken
    );
}

internal sealed class GitHubPluginMarketplaceRepositoryTransport(IHttpClientFactory clients)
    : IPluginMarketplaceRepositoryTransport
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        MaxDepth = 8,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    internal const string ClientName = "BlokeBot.PluginMarketplace.Repository";
    internal static readonly Uri TreeUrl = new(
        "https://api.github.com/repos/alsi-lawr/blokebot-plugins/git/trees/master?recursive=1"
    );

    public async ValueTask<PluginMarketplaceRepositoryDownload> DownloadAsync(
        string? entityTag,
        DateTimeOffset? modifiedSince,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var request = Request(TreeUrl);
            if (
                entityTag is not null
                && EntityTagHeaderValue.TryParse(entityTag, out var parsedEntityTag)
            )
            {
                request.Headers.IfNoneMatch.Add(parsedEntityTag);
            }
            request.Headers.IfModifiedSince = modifiedSince;
            using var response = await SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return entityTag is null && modifiedSince is null
                    ? new PluginMarketplaceRepositoryDownload.Failed()
                    : new PluginMarketplaceRepositoryDownload.NotModified(
                        response.Headers.ETag?.ToString(),
                        response.Content.Headers.LastModified
                    );
            }

            if (!response.IsSuccessStatusCode)
            {
                return new PluginMarketplaceRepositoryDownload.Failed();
            }

            await using var treeStream = await response.Content.ReadAsStreamAsync(
                cancellationToken
            );
            var tree = await JsonSerializer.DeserializeAsync<GitHubTreeDocument>(
                treeStream,
                _json,
                cancellationToken
            );
            if (!ValidTree(tree))
            {
                return new PluginMarketplaceRepositoryDownload.Failed();
            }

            var treeEntries = tree!.Tree;
            var entries = ImmutableArray.CreateBuilder<PluginMarketplaceRepositoryEntry>(
                treeEntries.Length
            );
            foreach (var source in treeEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var kind = Kind(source.Type, source.Mode);
                ReadOnlyMemory<byte> content = ReadOnlyMemory<byte>.Empty;
                if (IsManifest(source))
                {
                    if (
                        source.Size is < 0 or > PluginContractLimits.MaximumManifestBytes
                        || !ValidObjectId(source.Sha)
                    )
                    {
                        return new PluginMarketplaceRepositoryDownload.Failed();
                    }

                    var manifest = await DownloadBlobAsync(
                        source.Sha,
                        (int)source.Size,
                        cancellationToken
                    );
                    if (manifest is not { } downloadedManifest)
                    {
                        return new PluginMarketplaceRepositoryDownload.Failed();
                    }

                    content = downloadedManifest;
                }

                entries.Add(new(source.Path, kind, content));
            }

            return new PluginMarketplaceRepositoryDownload.Delivered(
                new(entries.MoveToImmutable()),
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or JsonException
                        or FormatException
                        or OverflowException
            )
        {
            return new PluginMarketplaceRepositoryDownload.Failed();
        }
    }

    private async ValueTask<ReadOnlyMemory<byte>?> DownloadBlobAsync(
        string objectId,
        int expectedBytes,
        CancellationToken cancellationToken
    )
    {
        var url = new Uri(
            $"https://api.github.com/repos/alsi-lawr/blokebot-plugins/git/blobs/{objectId}"
        );
        using var request = Request(url);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var encoded = await ReadBoundedAsync(
            response.Content,
            PluginContractLimits.MaximumManifestBytes * 2,
            cancellationToken
        );
        if (encoded is null)
        {
            return null;
        }

        var blob = JsonSerializer.Deserialize<GitHubBlobDocument>(encoded, _json);
        if (blob is null || blob.Encoding != "base64" || blob.Size != expectedBytes)
        {
            return null;
        }

        var content = Convert.FromBase64String(blob.Content);
        return content.Length == expectedBytes ? content : null;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) =>
        await clients
            .CreateClient(ClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    private static HttpRequestMessage Request(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BlokeBot", "0.13"));
        return request;
    }

    private static async ValueTask<byte[]?> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken
    )
    {
        if (content.Headers.ContentLength is { } contentLength && contentLength > maximumBytes)
        {
            return null;
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (target.Length <= maximumBytes)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return target.ToArray();
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return null;
    }

    private static bool ValidTree(GitHubTreeDocument? tree) =>
        tree is { Truncated: false }
        && tree.Tree is not null
        && tree.Tree.All(static entry => !string.IsNullOrEmpty(entry.Path));

    private static PluginMarketplaceRepositoryEntryKind Kind(string type, string mode) =>
        (type, mode) switch
        {
            ("blob", "100644" or "100755") => PluginMarketplaceRepositoryEntryKind.File,
            ("tree", "040000") => PluginMarketplaceRepositoryEntryKind.Directory,
            _ => PluginMarketplaceRepositoryEntryKind.Unsupported,
        };

    private static bool IsManifest(GitHubTreeEntry entry) =>
        Kind(entry.Type, entry.Mode) == PluginMarketplaceRepositoryEntryKind.File
        && entry.Path.EndsWith($"/{PluginPackage.ManifestPath}", StringComparison.Ordinal);

    private static bool ValidObjectId(string value) =>
        value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);

    private sealed record GitHubTreeDocument
    {
        public required GitHubTreeEntry[] Tree { get; init; }
        public required bool Truncated { get; init; }
    }

    private sealed record GitHubTreeEntry
    {
        public required string Path { get; init; }
        public required string Mode { get; init; }
        public required string Type { get; init; }
        public required string Sha { get; init; }
        public long Size { get; init; }
    }

    private sealed record GitHubBlobDocument
    {
        public required string Content { get; init; }
        public required string Encoding { get; init; }
        public required int Size { get; init; }
    }
}
