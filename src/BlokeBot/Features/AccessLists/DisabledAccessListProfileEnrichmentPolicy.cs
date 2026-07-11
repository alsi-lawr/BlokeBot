namespace BlokeBot.Features.AccessLists;

internal sealed class DisabledAccessListProfileEnrichmentPolicy
    : IAccessListProfileEnrichmentPolicy
{
    public Task<IReadOnlyList<AccessListEntryProfile>> EnrichAsync(
        IReadOnlyList<string> logins,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult<IReadOnlyList<AccessListEntryProfile>>(
            [.. logins.Select(login => new AccessListEntryProfile(login, null))]
        );
}
