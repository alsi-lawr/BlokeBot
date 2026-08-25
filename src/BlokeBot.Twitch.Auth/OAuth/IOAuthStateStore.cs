using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

public readonly record struct OAuthStateConsumed(CredentialEpoch CredentialEpoch);

public readonly record struct OAuthStateRejected;

/// <summary>
/// Issues and consumes OAuth state values.
/// </summary>
public interface IOAuthStateStore
{
    /// <summary>
    /// Issues a state value.
    /// </summary>
    /// <param name="credentialEpoch">The credential epoch at issuance.</param>
    /// <returns>The issued state value.</returns>
    string Issue(CredentialEpoch credentialEpoch);

    /// <summary>
    /// Consumes a state value.
    /// </summary>
    /// <param name="state">The state value to consume.</param>
    /// <returns>The typed state-consumption result.</returns>
    Result<OAuthStateConsumed, OAuthStateRejected> Consume(string state);
}
