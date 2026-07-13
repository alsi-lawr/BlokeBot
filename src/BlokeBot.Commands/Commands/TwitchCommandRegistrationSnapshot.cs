using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace BlokeBot.Commands;

internal sealed record TwitchCommandRegistrationSnapshot
{
    [SetsRequiredMembers]
    public TwitchCommandRegistrationSnapshot(IEnumerable<TwitchCommandRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        CommandCallbacks = registrations
            .Select(registration => registration.Configure)
            .ToImmutableArray();
    }

    public required ImmutableArray<Action<ITwitchCommandBuilder>> CommandCallbacks { get; init; }
}
