using Shouldly;
using TUnit.Core;

namespace BlokeBot.Commands.Tests;

public sealed class ChatCommandCompositionTests
{
    [Test]
    public void RegisteredCallbackModuleAndFilter_ConstructingPlan_ComposesAllParts()
    {
        var registrations = new ChatCommandRegistrationSnapshot([
            Registration(commands =>
                commands
                    .UseFilter<AllowFilter>()
                    .Map("callback", (_, _, _) => ValueTask.CompletedTask)
            ),
        ]);
        var filter = new AllowFilter();

        var registry = new ChatCommandRegistry(registrations, [new CompositionModule()], [filter]);

        registry.Plan.Routes.Keys.ShouldContain("callback");
        registry.Plan.Routes.Keys.ShouldContain("module");
        registry.Plan.Filters.ShouldBe([filter]);
    }

    [Test]
    public void SelectedFilterWithoutRegistration_ConstructingPlan_RejectsMissingFilter()
    {
        var registrations = new ChatCommandRegistrationSnapshot([
            Registration(commands => commands.UseFilter<AllowFilter>()),
        ]);

        var exception = Should.Throw<InvalidOperationException>(() =>
            new ChatCommandRegistry(registrations, [], [])
        );

        exception.Message.ShouldContain(typeof(AllowFilter).FullName!);
        exception.Message.ShouldContain("registered explicitly");
    }

    [Test]
    public void CallerOwnedRegistrations_MutatingAfterSnapshot_PreservesOriginalCallbacksAndOrder()
    {
        List<string> callbackOrder = [];
        List<ChatCommandRegistration> registrations =
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
        var snapshot = new ChatCommandRegistrationSnapshot(registrations);

        registrations.Reverse();
        registrations.Add(
            Registration(commands =>
            {
                callbackOrder.Add("later");
                commands.Map("later", (_, _, _) => ValueTask.CompletedTask);
            })
        );

        var registry = new ChatCommandRegistry(snapshot, [], []);

        callbackOrder.ShouldBe(["first", "second"]);
        registry.Plan.Routes.Keys.ShouldBe(["first", "second"], ignoreOrder: true);
        registry.Plan.Routes.Keys.ShouldNotContain("later");
    }

    private static ChatCommandRegistration Registration(Action<IChatCommandBuilder> configure)
    {
        return new() { Configure = configure };
    }

    private sealed class CompositionModule : IChatCommandModule
    {
        public void AddCommands(IChatCommandBuilder commands)
        {
            commands.Map("module", (_, _, _) => ValueTask.CompletedTask);
        }
    }

    private sealed class AllowFilter : IChatCommandFilter;
}
