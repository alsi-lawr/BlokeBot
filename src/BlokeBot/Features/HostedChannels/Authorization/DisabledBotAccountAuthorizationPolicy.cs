namespace BlokeBot.Features.HostedChannels.Authorization;

internal sealed class DisabledBotAccountAuthorizationPolicy(BotSettings settings)
    : IBotAccountAuthorizationPolicy
{
    public Task<BotAccountAuthorizationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(
            new BotAccountAuthorizationStatus(
                settings.Identity.BotUsername,
                null,
                null,
                BotAccountAuthorizationState.Disabled,
                settings.Identity.Scopes,
                [],
                [],
                "The Twitch bot runner is not configured."
            )
        );
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
