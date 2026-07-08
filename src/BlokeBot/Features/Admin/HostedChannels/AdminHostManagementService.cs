using BlokeBot.Auth.Users;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Hosts;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Admin.HostedChannels;

internal sealed class AdminHostManagementService(
    UserLookupService users,
    BotHostProvisioningService hostProvisioning,
    BotHostRemovalService hostRemoval,
    HostedChannelRuntimeControlService runtime,
    HostedChannelDirectoryService hostedChannels
)
{
    public async Task<AdminHostOperationResult> CreateHostAsync(string login, CancellationToken ct)
    {
        TwitchHelixUser? user;
        try
        {
            user = await users.FindByLoginAsync(login, ct);
        }
        catch (InvalidOperationException)
        {
            return new AdminHostOperationResult(
                false,
                "Authorize the bot account before creating hosted channels."
            );
        }
        catch (HttpRequestException)
        {
            return new AdminHostOperationResult(false, "Twitch user lookup failed.");
        }

        if (user is null || string.IsNullOrWhiteSpace(user.Login))
            return new AdminHostOperationResult(false, "Twitch user not found.");

        var displayName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Login
            : user.DisplayName;
        await hostProvisioning.EnsureHostAsync(
            user.Login,
            user.Id,
            displayName,
            user.ProfileImageUrl,
            ct
        );
        return new AdminHostOperationResult(true, $"Created hosted channel for {displayName}.");
    }

    public async Task<AdminHostOperationResult> RemoveHostAsync(int hostId, CancellationToken ct)
    {
        await runtime.StopAsync(hostId, ct);
        var removed = await hostRemoval.RemoveAsync(hostId, ct);
        return new AdminHostOperationResult(
            true,
            removed ? "Hosted channel removed." : "Hosted channel was already removed."
        );
    }

    public async Task<AdminHostOperationResult> StartBotAsync(int hostId, CancellationToken ct) =>
        await ApplyRuntimeOperationAsync(hostId, runtime.StartAsync, ct);

    public async Task<AdminHostOperationResult> StopBotAsync(int hostId, CancellationToken ct) =>
        await ApplyRuntimeOperationAsync(hostId, runtime.StopAsync, ct);

    public AdminHostOperationResult RefreshPendingRuntime(
        int hostId,
        IReadOnlyList<HostedChannelAdminView> hosts
    )
    {
        var host = hosts.FirstOrDefault(x => x.Id == hostId);
        var state = host?.RuntimeState;
        return new AdminHostOperationResult(
            true,
            RuntimeStatusMessage(state),
            IsRuntimeTransitionPending(state) ? hostId : null
        );
    }

    private async Task<AdminHostOperationResult> ApplyRuntimeOperationAsync(
        int hostId,
        Func<int, CancellationToken, Task<HostedChannelRuntimeControlResult>> operation,
        CancellationToken ct
    )
    {
        var result = await operation(hostId, ct);
        var hosts = await hostedChannels.LoadHostedChannelsAsync(ct);
        var currentState = hosts.FirstOrDefault(host => host.Id == hostId)?.RuntimeState;
        return new AdminHostOperationResult(
            result.Succeeded,
            result.Succeeded ? RuntimeStatusMessage(currentState) : result.Message,
            result.Succeeded && IsRuntimeTransitionPending(currentState) ? hostId : null
        );
    }

    private static string RuntimeStatusMessage(BotChannelRuntimeState? state) =>
        state switch
        {
            BotChannelRuntimeState.Starting => "Bot starting.",
            BotChannelRuntimeState.Started => "Bot started.",
            BotChannelRuntimeState.Stopping => "Bot stopping.",
            _ => "Bot stopped.",
        };

    private static bool IsRuntimeTransitionPending(BotChannelRuntimeState? state) =>
        state is BotChannelRuntimeState.Starting or BotChannelRuntimeState.Stopping;
}

public sealed record AdminHostOperationResult(
    bool Succeeded,
    string Message,
    int? PendingRuntimeHostId = null
);
