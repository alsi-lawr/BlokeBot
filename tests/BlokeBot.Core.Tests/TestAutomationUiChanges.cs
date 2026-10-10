using BlokeBot.Core.Features.Automations;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlokeBot.Core.Tests;

internal static class TestAutomationUiChanges
{
    internal static AutomationUiChanges Create() =>
        new(TestEventBus.Create<AutomationUiChange>(), NullLogger<AutomationUiChanges>.Instance);
}
