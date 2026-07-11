namespace BlokeBot.Features.AccessLists;

public sealed class AccessListProfileResolver(
    IAccessListProfileEnrichmentPolicy enrichment
)
{
    public async Task<IReadOnlyList<AccessListEntryProfile>> ResolveAsync(
        IEnumerable<string> logins,
        CancellationToken ct
    )
    {
        var entries = logins
            .Where(login => !string.IsNullOrWhiteSpace(login))
            .Select(login => login.Trim())
            .ToArray();
        if (entries.Length == 0)
            return [];

        return await enrichment.EnrichAsync(entries, ct);
    }
}
