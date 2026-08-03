using System.Collections;

namespace BlokeBot.Twitch.Auth;

public sealed class OAuthAuthorizationScopeSet
    : IReadOnlyList<string>,
        IEquatable<OAuthAuthorizationScopeSet>
{
    private readonly OAuthScopeSet _scopes;

    private OAuthAuthorizationScopeSet(OAuthScopeSet scopes) => _scopes = scopes;

    public int Count => _scopes.Count;

    public string this[int index] => _scopes[index];

    public static OAuthAuthorizationScopeSet Create(IEnumerable<string> scopes)
    {
        var normalized = OAuthScopeSet.Create(scopes);
        return normalized.Count == 0
            ? throw new ArgumentException(
                "OAuth authorization scopes must contain at least one value.",
                nameof(scopes)
            )
            : new OAuthAuthorizationScopeSet(normalized);
    }

    public string Serialize() => _scopes.Serialize();

    public bool Equals(OAuthAuthorizationScopeSet? other) =>
        other is not null && _scopes.Equals(other._scopes);

    public override bool Equals(object? obj) =>
        obj is OAuthAuthorizationScopeSet other && Equals(other);

    public override int GetHashCode() => _scopes.GetHashCode();

    public IEnumerator<string> GetEnumerator() => _scopes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
