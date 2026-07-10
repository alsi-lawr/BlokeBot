namespace BlokeBot.Persistence.Models;

[Flags]
public enum HostFeatureFlags : ulong
{
    None = 0UL,
    Guessing = 1UL << 0,
    Points = 1UL << 1,
    CustomCommands = 1UL << 2,
    All = Guessing | Points | CustomCommands,
}
