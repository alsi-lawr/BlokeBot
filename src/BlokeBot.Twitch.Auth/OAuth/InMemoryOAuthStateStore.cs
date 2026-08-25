using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

internal sealed class InMemoryOAuthStateStore : IOAuthStateStore
{
    private readonly Dictionary<string, CredentialEpoch> _states = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public string Issue(CredentialEpoch credentialEpoch)
    {
        var state = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            _states.Add(state, credentialEpoch);
        }

        return state;
    }

    public Result<OAuthStateConsumed, OAuthStateRejected> Consume(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return Result<OAuthStateConsumed, OAuthStateRejected>.Error(new OAuthStateRejected());
        }

        lock (_gate)
        {
            return _states.Remove(state, out var credentialEpoch)
                ? Result<OAuthStateConsumed, OAuthStateRejected>.Success(
                    new OAuthStateConsumed(credentialEpoch)
                )
                : Result<OAuthStateConsumed, OAuthStateRejected>.Error(new OAuthStateRejected());
        }
    }
}
