using Shouldly;
using TUnit.Core;

namespace BlokeBot.Commands.Tests;

public sealed class TwitchCommandCompositionTests
{
    [Test]
    public void RegisteredCallbackModuleAndFilter_ConstructingPlan_ComposesAllParts()
    {
        var registrations = new TwitchCommandRegistrationSnapshot(
        [
            Registration(commands =>
                commands
                    .UseFilter<AllowFilter>()
                    .Map("callback", (_, _, _) => ValueTask.CompletedTask)
            ),
        ]
        );
        var filter = new AllowFilter();

        var registry = new TwitchCommandRegistry(registrations, [new CompositionModule()], [filter]);

        registry.Plan.Routes.Keys.ShouldContain("callback");
        registry.Plan.Routes.Keys.ShouldContain("module");
        registry.Plan.Filters.ShouldBe([filter]);
    }

    [Test]
    public void SelectedFilterWithoutRegistration_ConstructingPlan_RejectsMissingFilter()
    {
        var registrations = new TwitchCommandRegistrationSnapshot(
        [
            Registration(commands => commands.UseFilter<AllowFilter>()),
        ]
        );

        var exception = Should.Throw<InvalidOperationException>(() =>
            new TwitchCommandRegistry(registrations, [], [])
        );

        exception.Message.ShouldContain(typeof(AllowFilter).FullName!);
        exception.Message.ShouldContain("registered explicitly");
    }

    [Test]
    public void CallerOwnedRegistrations_MutatingAfterSnapshot_PreservesOriginalCallbacksAndOrder()
    {
        List<string> callbackOrder = [];
        List<TwitchCommandRegistration> registrations =
        [
            Registration(commands =>
            {
                callbackOrder.Add("first");
                commands.Map("first", (_, _, _) => ValueTask.CompletedTask);
            }),
            Registration(commands =>
            {
                callbackOrder.Add("second");
                commands.Map("second", (_, _, _) => ValueTask.CompletedTask);
            }),
        ];
        var snapshot = new TwitchCommandRegistrationSnapshot(registrations);

        registrations.Reverse();
        registrations.Add(
            Registration(commands =>
            {
                callbackOrder.Add("later");
                commands.Map("later", (_, _, _) => ValueTask.CompletedTask);
            })
        );

        var registry = new TwitchCommandRegistry(snapshot, [], []);

        callbackOrder.ShouldBe(["first", "second"]);
        registry.Plan.Routes.Keys.ShouldBe(["first", "second"], ignoreOrder: true);
        registry.Plan.Routes.Keys.ShouldNotContain("later");
    }

    private static TwitchCommandRegistration Registration(
        Action<ITwitchCommandBuilder> configure
    ) => new() { Configure = configure };

    private sealed class CompositionModule : ITwitchCommandModule
    {
        public void AddCommands(ITwitchCommandBuilder commands) =>
            commands.Map("module", (_, _, _) => ValueTask.CompletedTask);
    }

    private sealed class AllowFilter : ITwitchCommandFilter;
}
