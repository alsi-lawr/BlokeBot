using System.Collections;

namespace BlokeBot.Twitch.Auth;

public sealed class OAuthAuthorizationScopeSet
    : IReadOnlyList<string>,
        IEquatable<OAuthAuthorizationScopeSet>
{
    private readonly OAuthScopeSet _scopes;

    private OAuthAuthorizationScopeSet(OAuthScopeSet scopes)
    {
        _scopes = scopes;
    }

    public int Count => _scopes.Count;

    public string this[int index] => _scopes[index];

    public static OAuthAuthorizationScopeSet Create(IEnumerable<string> scopes)
    {
        var normalized = OAuthScopeSet.Create(scopes);
        if (normalized.Count == 0)
        {
            throw new ArgumentException(
                "OAuth authorization scopes must contain at least one value.",
                nameof(scopes)
            );
        }

        return new OAuthAuthorizationScopeSet(normalized);
    }

    public string Serialize()
    {
        return _scopes.Serialize();
    }

    public bool Equals(OAuthAuthorizationScopeSet? other)
    {
        return other is not null && _scopes.Equals(other._scopes);
    }

    public override bool Equals(object? obj)
    {
        return obj is OAuthAuthorizationScopeSet other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _scopes.GetHashCode();
    }

    public IEnumerator<string> GetEnumerator()
    {
        return _scopes.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
