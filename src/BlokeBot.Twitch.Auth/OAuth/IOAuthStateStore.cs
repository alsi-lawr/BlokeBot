using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

public readonly record struct OAuthStateConsumed;

public readonly record struct OAuthStateRejected;

/// <summary>
/// Issues and consumes OAuth state values.
/// </summary>
public interface IOAuthStateStore
{
    /// <summary>
    /// Issues a state value.
    /// </summary>
    /// <returns>The issued state value.</returns>
    string Issue();

    /// <summary>
    /// Consumes a state value.
    /// </summary>
    /// <param name="state">The state value to consume.</param>
    /// <returns>The typed state-consumption result.</returns>
    Result<OAuthStateConsumed, OAuthStateRejected> Consume(string state);
}
