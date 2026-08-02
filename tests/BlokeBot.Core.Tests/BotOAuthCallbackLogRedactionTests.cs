using BlokeBot.Core.Auth.Sessions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BotOAuthCallbackLogRedactionTests : BotOAuthEndpointIntegrationTestBase
{
    [Test]
    public async Task EveryBotCallback_RedactsQueryValuesFromEveryCapturedLogCategory()
    {
        const string Error = "bot-error-sentinel";
        const string Code = "bot-code-sentinel";
        const string State = "bot-state-sentinel";
        const string Query = $"?error={Error}&code={Code}&state={State}";
        using var logs = new CallbackLogCapture();
        await using var host = await EndpointHost.StartAsync(
            configured: true,
            selectedRole: AuthRole.Streamer,
            login: "streamer",
            endpointScenario: EndpointScenario.BroadcasterTransportFailure,
            logs: logs
        );

        using var global = await host.Client.GetAsync($"/oauth/callback{Query}");
        using var channel = await host.Client.SendAsync(
            CallbackRequest($"/oauth/channel-bot/callback{Query}", "BlokeBot.ChannelBotState")
        );
        var hostState = host.IssueHostBotState();
        using var hostBot = await host.Client.GetAsync(
            $"/oauth/callback?error={Error}&code={Code}&state={hostState}"
        );
        var broadcasterState = host.IssueBroadcasterState(1);
        using var broadcaster = await host.Client.GetAsync(
            $"/oauth/callback?code={Code}&state={broadcasterState}"
        );

        global.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadGateway);
        channel.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadGateway);
        hostBot.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadGateway);
        broadcaster.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadGateway);
        logs.Entries.ShouldNotBeEmpty();
        foreach (var entry in logs.Entries)
        {
            entry.Message.ShouldNotContain(Error);
            entry.Message.ShouldNotContain(Code);
            entry.Message.ShouldNotContain(State);
            entry.Message.ShouldNotContain(hostState);
            entry.Message.ShouldNotContain(broadcasterState);
            entry
                .Properties.Values.Any(value =>
                    value is not null
                    && value.ToString()!.Contains("-sentinel", StringComparison.Ordinal)
                )
                .ShouldBeFalse();
        }
    }
}
