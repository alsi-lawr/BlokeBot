namespace Alsi.TwitchBot;

/// <summary>
/// Adds commands to a Twitch command builder.
/// </summary>
public interface ITwitchCommandModule
{
    /// <summary>
    /// Registers the module commands.
    /// </summary>
    /// <param name="commands">The command builder to register with.</param>
    void AddCommands(ITwitchCommandBuilder commands);
}
