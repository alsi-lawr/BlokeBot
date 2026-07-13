namespace BlokeBot.Commands;

/// <summary>
/// Adds commands to a Twitch command builder.
/// </summary>
public interface IChatCommandModule
{
    /// <summary>
    /// Registers the module commands.
    /// </summary>
    /// <param name="commands">The command builder to register with.</param>
    void AddCommands(IChatCommandBuilder commands);
}
