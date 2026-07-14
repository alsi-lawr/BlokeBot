using System.Collections;
using System.Collections.Immutable;

namespace BlokeBot.Twitch.Auth;

public sealed class OAuthScopeSet : IReadOnlyList<string>, IEquatable<OAuthScopeSet>
{
    private readonly ImmutableArray<string> _scopes;

    private OAuthScopeSet(ImmutableArray<string> scopes)
    {
        _scopes = scopes;
    }

    public int Count => _scopes.Length;

    public int Length => _scopes.Length;

    public string this[int index] => _scopes[index];

    public static OAuthScopeSet Create(IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in scopes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "OAuth scopes cannot contain null or blank values.",
                    nameof(scopes)
                );
            }

            var scope = value.Trim().ToLowerInvariant();
            if (!IsValid(scope))
            {
                throw new ArgumentException(
                    "OAuth scopes must use Twitch scope token syntax.",
                    nameof(scopes)
                );
            }

            normalized.Add(scope);
        }

        if (normalized.Count == 0)
        {
            throw new ArgumentException(
                "OAuth scopes must contain at least one value.",
                nameof(scopes)
            );
        }

        return new OAuthScopeSet(ImmutableArray.CreateRange(normalized));
    }

    public string Serialize()
    {
        return string.Join(' ', _scopes);
    }

    public bool Equals(OAuthScopeSet? other)
    {
        return other is not null && _scopes.SequenceEqual(other._scopes);
    }

    public override bool Equals(object? obj)
    {
        return obj is OAuthScopeSet other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var scope in _scopes)
        {
            hash.Add(scope, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<string> GetEnumerator()
    {
        return ((IEnumerable<string>)_scopes).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    internal static bool IsValid(string scope)
    {
        return scope.Length > 0
            && scope[0] != ':'
            && scope[^1] != ':'
            && !scope.Contains("::", StringComparison.Ordinal)
            && scope.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is ':' or '_'
            );
    }
}
