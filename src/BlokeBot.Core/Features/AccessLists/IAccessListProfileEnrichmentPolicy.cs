namespace BlokeBot.Core.Features.AccessLists;

/// <summary>
/// Applies the explicitly selected profile-enrichment behavior to access-list logins.
/// </summary>
public interface IAccessListProfileEnrichmentPolicy
{
    /// <summary>
    /// Produces one access-list profile for each normalized input login.
    /// </summary>
    Task<IReadOnlyList<AccessListEntryProfile>> EnrichAsync(
        IReadOnlyList<string> logins,
        CancellationToken cancellationToken
    );
}
