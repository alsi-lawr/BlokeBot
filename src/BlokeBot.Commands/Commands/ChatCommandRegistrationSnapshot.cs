using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace BlokeBot.Commands;

internal sealed record ChatCommandRegistrationSnapshot
{
    [SetsRequiredMembers]
    public ChatCommandRegistrationSnapshot(IEnumerable<ChatCommandRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        CommandCallbacks = registrations
            .Select(registration => registration.Configure)
            .ToImmutableArray();
    }

    public required ImmutableArray<Action<IChatCommandBuilder>> CommandCallbacks { get; init; }
}
