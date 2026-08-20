namespace BlokeBot.Persistence.Privacy;

/// <summary>
/// Identifies the Twitch identity a privacy request concerns. A Twitch user id is authoritative;
/// login-only attribution requires a unique, non-ambiguous viewer-passport claim.
/// </summary>
public sealed record PrivacySubject
{
    private PrivacySubject(string? twitchUserId, string? login)
    {
        TwitchUserId = twitchUserId;
        Login = login;
    }

    public string? TwitchUserId { get; }

    public string? Login { get; }

    internal string IdIdentityKey =>
        TwitchUserId is null ? ViewerPrivacyService.UnmatchableValue : $"id:{TwitchUserId}";

    public static PrivacySubject Create(string? twitchUserId, string? login)
    {
        var normalizedId = string.IsNullOrWhiteSpace(twitchUserId) ? null : twitchUserId.Trim();
        var normalizedLogin = NormalizeLogin(login);
        return normalizedId is null && normalizedLogin is null
            ? throw new ArgumentException(
                "A privacy subject needs a Twitch user id, a login, or both."
            )
            : new PrivacySubject(normalizedId, normalizedLogin);
    }

    private static string? NormalizeLogin(string? login)
    {
        var trimmed = login?.Trim().TrimStart('@', '#');
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToLowerInvariant();
    }
}

public sealed record ViewerDataExport(IReadOnlyDictionary<string, IReadOnlyList<object>> Sections);

public sealed record ViewerErasureReport(IReadOnlyDictionary<string, int> ChangedRows)
{
    public int TotalChangedRows => ChangedRows.Values.Sum();
}

/// <summary>
/// Locates, exports, and erases the data attributable to one Twitch identity across every
/// persisted feature. Erasure follows the accepted policy: rows that exist only for the subject
/// are deleted; rows that must remain for non-personal aggregate, ledger, or audit integrity keep
/// their numbers but lose identity and free-text fields, replaced by a shared non-reversible
/// token. Reruns are no-ops because erased rows no longer match the subject.
/// </summary>
public static partial class ViewerPrivacyService
{
    public const string ErasedToken = "[erased]";

    // Comparisons against a missing identity part must match nothing, never NULL columns, so an
    // absent part becomes a value no Twitch id or login can contain.
    internal const string UnmatchableValue = "\u0001";

    public static async Task<ViewerDataExport> ExportAsync(
        BlokeBotDbContext db,
        PrivacySubject subject,
        int? hostId,
        CancellationToken ct
    ) =>
        await ExecuteConsistentSnapshotAsync(
            db,
            static () => new ViewerDataExport(new Dictionary<string, IReadOnlyList<object>>()),
            () => ExportInSnapshotAsync(db, subject, hostId, ct),
            ct
        );

    public static async Task<ViewerErasureReport> EraseAsync(
        BlokeBotDbContext db,
        PrivacySubject subject,
        int? hostId,
        CancellationToken ct
    ) =>
        await ExecuteConsistentSnapshotAsync(
            db,
            static () =>
                new ViewerErasureReport(new Dictionary<string, int>(StringComparer.Ordinal)),
            () => EraseInSnapshotAsync(db, subject, hostId, ct),
            ct
        );
}
