using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchBotAccountProviderSelectionTests
{
    [Test]
    public void DefaultProvider_Selected_ProducesDefaultPolicy()
    {
        var policy = new TwitchBotAccountProviderSelection().UseDefaultProvider().RequireSingle();

        policy.Kind.ShouldBe(TwitchBotAccountProviderKind.Default);
        policy.ProviderType.ShouldBe(typeof(DefaultTwitchBotAccountProvider));
    }

    [Test]
    public void HostedChannelProvider_Selected_ProducesHostedChannelPolicy()
    {
        var policy = new TwitchBotAccountProviderSelection()
            .UseHostedChannelProvider<HostedChannelProvider>()
            .RequireSingle();

        policy.Kind.ShouldBe(TwitchBotAccountProviderKind.HostedChannel);
        policy.ProviderType.ShouldBe(typeof(HostedChannelProvider));
    }

    [Test]
    public void CustomProvider_Selected_ProducesCustomPolicy()
    {
        var policy = new TwitchBotAccountProviderSelection()
            .UseCustomProvider<CustomProvider>()
            .RequireSingle();

        policy.Kind.ShouldBe(TwitchBotAccountProviderKind.Custom);
        policy.ProviderType.ShouldBe(typeof(CustomProvider));
    }

    [Test]
    public void NoProvider_Selected_RejectsMissingPolicy()
    {
        var selection = new TwitchBotAccountProviderSelection();

        var exception = Should.Throw<InvalidOperationException>(selection.RequireSingle);

        exception.Message.ShouldContain("none was selected");
    }

    [Test]
    public void ConflictingProviders_SelectedInEitherOrder_RejectsPolicy()
    {
        var defaultThenCustom = new TwitchBotAccountProviderSelection()
            .UseDefaultProvider()
            .UseCustomProvider<CustomProvider>();
        var customThenDefault = new TwitchBotAccountProviderSelection()
            .UseCustomProvider<CustomProvider>()
            .UseDefaultProvider();

        var firstException = Should.Throw<InvalidOperationException>(
            defaultThenCustom.RequireSingle
        );
        var secondException = Should.Throw<InvalidOperationException>(
            customThenDefault.RequireSingle
        );

        firstException.Message.ShouldContain("2 were selected");
        secondException.Message.ShouldContain("2 were selected");
    }

    private sealed class HostedChannelProvider : ITwitchBotAccountProvider
    {
        public ValueTask<TwitchBotAccount> GetBotAccountAsync(
            string channelLogin,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult(new TwitchBotAccount(channelLogin, "hosted-token"));
        }
    }

    private sealed class CustomProvider : ITwitchBotAccountProvider
    {
        public ValueTask<TwitchBotAccount> GetBotAccountAsync(
            string channelLogin,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult(new TwitchBotAccount(channelLogin, "custom-token"));
        }
    }
}
