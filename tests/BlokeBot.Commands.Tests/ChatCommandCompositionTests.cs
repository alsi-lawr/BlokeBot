using Shouldly;

namespace BlokeBot.Commands.Tests;

public sealed class ChatCommandCompositionTests
{
    [Test]
    public void RegisteredCallbackModuleAndFilter_ConstructingPlan_ComposesAllParts()
    {
        ChatCommandRegistration[] registrations =
        [
            Registration(commands =>
                commands
                    .UseFilter<AllowFilter>()
                    .Map("callback", (_, _, _) => ValueTask.CompletedTask)
            ),
        ];
        var filter = new AllowFilter();

        var registry = new ChatCommandRegistry(registrations, [new CompositionModule()], [filter]);

        registry.Plan.Routes.Keys.ShouldContain("callback");
        registry.Plan.Routes.Keys.ShouldContain("module");
        registry.Plan.Filters.ShouldBe([filter]);
    }

    [Test]
    public void SelectedFilterWithoutRegistration_ConstructingPlan_RejectsMissingFilter()
    {
        ChatCommandRegistration[] registrations =
        [
            Registration(commands => commands.UseFilter<AllowFilter>()),
        ];

        var exception = Should.Throw<InvalidOperationException>(() =>
            new ChatCommandRegistry(registrations, [], [])
        );

        exception.Message.ShouldContain(typeof(AllowFilter).FullName!);
        exception.Message.ShouldContain("registered explicitly");
    }

    private static ChatCommandRegistration Registration(Action<IChatCommandBuilder> configure) =>
        new() { Configure = configure };

    private sealed class CompositionModule : IChatCommandModule
    {
        public void AddCommands(IChatCommandBuilder commands) =>
            commands.Map("module", (_, _, _) => ValueTask.CompletedTask);
    }

    private sealed class AllowFilter : IChatCommandFilter;
}
