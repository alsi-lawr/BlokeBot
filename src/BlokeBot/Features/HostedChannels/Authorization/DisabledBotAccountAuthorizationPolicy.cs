namespace BlokeBot.Features.HostedChannels.Authorization;

internal sealed class DisabledBotAccountAuthorizationPolicy(TwitchBotSettings settings)
    : IBotAccountAuthorizationPolicy
{
    public Task<BotAccountAuthorizationStatus> GetStatusAsync(
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
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

    public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
