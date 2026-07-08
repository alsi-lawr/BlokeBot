using BlokeBot.Auth.Sessions;
using BlokeBot.Hosts;
using Microsoft.AspNetCore.Components.Authorization;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class BlokeBotPageContextTests
{
    [Test]
    public async Task FromAsync_extracts_typed_session_fields()
    {
        var selectedHost = new BotHostChoice(42, "streamer", "Streamer", AuthRole.Streamer);
        var principal = TestPrincipals.BlokeBotUser(
            login: "streamer",
            role: AuthRole.Streamer,
            availableHosts: [selectedHost],
            selectedHostId: selectedHost.Id.ToString()
        );

        var context = await new BlokeBotPageContextAccessor()
            .FromAsync(Task.FromResult(new AuthenticationState(principal)));

        context.Session.IsAuthenticated.ShouldBeTrue();
        context.ActorLogin.ShouldBe("streamer");
        context.IsBotAccount.ShouldBeFalse();
        context.HostSelection.ShouldNotBeNull();
        context.SelectedHost.ShouldNotBeNull();
        context.SelectedHost.Id.ShouldBe(selectedHost.Id);
        context.SelectedHost.Login.ShouldBe(selectedHost.Login);
        context.SelectedHost.Role.ShouldBe(selectedHost.Role);
        context.HasSelectedHost.ShouldBeTrue();
    }
}
