namespace BlokeBot.Twitch.Auth;

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
    /// <returns><see langword="true" /> when the value was valid and unused.</returns>
    bool Consume(string state);
}
