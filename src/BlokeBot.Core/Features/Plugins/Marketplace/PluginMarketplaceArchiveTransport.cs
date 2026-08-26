using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

internal abstract record PluginMarketplaceArchiveDownload
{
    private PluginMarketplaceArchiveDownload() { }

    internal sealed record Delivered : PluginMarketplaceArchiveDownload;

    internal sealed record Failed : PluginMarketplaceArchiveDownload;
}

internal interface IPluginMarketplaceArchiveTransport
{
    ValueTask<PluginMarketplaceArchiveDownload> DownloadAsync(
        Uri repository,
        PluginGitTag tag,
        string destination,
        CancellationToken cancellationToken
    );
}

internal sealed class GitHubPluginMarketplaceArchiveTransport(IHttpClientFactory clients)
    : IPluginMarketplaceArchiveTransport
{
    internal const string ClientName = "BlokeBot.PluginMarketplace.Archives";

    public async ValueTask<PluginMarketplaceArchiveDownload> DownloadAsync(
        Uri repository,
        PluginGitTag tag,
        string destination,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var components = repository.AbsolutePath.Trim('/').Split('/');
        if (
            !repository.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !repository.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || components.Length != 2
        )
        {
            return new PluginMarketplaceArchiveDownload.Failed();
        }

        var url = new Uri(
            $"https://codeload.github.com/{components[0]}/{components[1]}/tar.gz/refs/tags/"
                + Uri.EscapeDataString(tag.Value)
        );
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await clients
                .CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new PluginMarketplaceArchiveDownload.Failed();
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
            return new PluginMarketplaceArchiveDownload.Delivered();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Delete(destination);
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Delete(destination);
            return new PluginMarketplaceArchiveDownload.Failed();
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The staging cleanup pass retries deletion without exposing source details.
        }
    }
}
