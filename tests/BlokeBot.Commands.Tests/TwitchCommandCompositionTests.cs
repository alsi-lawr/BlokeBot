using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Commands.Tests;

public sealed class TwitchCommandCompositionTests
{
    [Test]
    public void RegisteredCallbackModuleAndFilter_ConstructingPlan_ComposesAllParts()
    {
        var registrations = new TwitchCommandRegistrationOptions();
        registrations.CommandCallbacks.Add(commands =>
            commands
                .UseFilter<AllowFilter>()
                .Map("callback", (_, _, _) => ValueTask.CompletedTask)
        );
        var filter = new AllowFilter();

        var registry = new TwitchCommandRegistry(
            Options.Create(registrations),
            [new CompositionModule()],
            [filter]
        );

        registry.Plan.Routes.Keys.ShouldContain("callback");
        registry.Plan.Routes.Keys.ShouldContain("module");
        registry.Plan.Filters.ShouldBe([filter]);
    }

    [Test]
    public void SelectedFilterWithoutRegistration_ConstructingPlan_RejectsMissingFilter()
    {
        var registrations = new TwitchCommandRegistrationOptions();
        registrations.CommandCallbacks.Add(commands => commands.UseFilter<AllowFilter>());

        var exception = Should.Throw<InvalidOperationException>(() =>
            new TwitchCommandRegistry(Options.Create(registrations), [], [])
        );

        exception.Message.ShouldContain(typeof(AllowFilter).FullName!);
        exception.Message.ShouldContain("registered explicitly");
    }

    private sealed class CompositionModule : ITwitchCommandModule
    {
        public void AddCommands(ITwitchCommandBuilder commands) =>
            commands.Map("module", (_, _, _) => ValueTask.CompletedTask);
    }

    private sealed class AllowFilter : ITwitchCommandFilter;
}
