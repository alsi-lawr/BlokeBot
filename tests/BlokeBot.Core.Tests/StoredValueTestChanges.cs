using BlokeBot.Core.Features.CustomCommands;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlokeBot.Core.Tests;

internal static class StoredValueTestChanges
{
    internal static CustomStoredValueChanges Create() =>
        new(
            TestEventBus.CreateContinueAndRecord<CustomStoredValueChange>().Events,
            NullLogger<CustomStoredValueChanges>.Instance
        );
}
