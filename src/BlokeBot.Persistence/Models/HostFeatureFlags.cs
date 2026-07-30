namespace BlokeBot.Persistence.Models;

[Flags]
public enum HostFeatureFlags : ulong
{
    None = 0UL,
    Guessing = 1UL << 0,
    Points = 1UL << 1,
    CustomCommands = 1UL << 2,
    NativeTwitch = 1UL << 3,
    Overlays = 1UL << 4,
    All = Guessing | Points | CustomCommands | NativeTwitch | Overlays,
}
