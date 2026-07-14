using BlokeBot.Auth.Users;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Functional;
using BlokeBot.Hosts;

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
        Option<UserIdentity> user;
        try
        {
            user = await users.FindByLoginAsync(login, ct);
        }
        catch (InvalidOperationException)
        {
            return new AdminHostOperationResult(
                false,
                "Connect the bot account before adding channels."
            );
        }
        catch (HttpRequestException)
        {
            return new AdminHostOperationResult(
                false,
                "Twitch could not look up that user. Try again."
            );
        }

        return await user.Match(
            identity => CreateHostAsync(identity, ct),
            () => Task.FromResult(new AdminHostOperationResult(false, "Twitch user not found."))
        );
    }

    public async Task<AdminHostOperationResult> RemoveHostAsync(int hostId, CancellationToken ct)
    {
        await runtime.StopAsync(hostId, ct);
        var removed = await hostRemoval.RemoveAsync(hostId, ct);
        return new AdminHostOperationResult(
            true,
            removed ? "Channel removed." : "Channel was already removed."
        );
    }

    public async Task<AdminHostOperationResult> StartBotAsync(int hostId, CancellationToken ct)
    {
        return await ApplyRuntimeOperationAsync(hostId, runtime.StartAsync, ct);
    }

    public async Task<AdminHostOperationResult> StopBotAsync(int hostId, CancellationToken ct)
    {
        return await ApplyRuntimeOperationAsync(hostId, runtime.StopAsync, ct);
    }

    public AdminHostOperationResult RefreshPendingRuntime(
        int hostId,
        IReadOnlyList<HostedChannelAdminView> hosts
    )
    {
        var host = hosts.FirstOrDefault(x => x.Id == hostId);
        var lifecycle = host?.Lifecycle;
        return new AdminHostOperationResult(
            true,
            RuntimeStatusMessage(lifecycle),
            IsRuntimeTransitionPending(lifecycle) ? hostId : null
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
        var currentLifecycle = hosts.FirstOrDefault(host => host.Id == hostId)?.Lifecycle;
        return new AdminHostOperationResult(
            result.Succeeded,
            result.Succeeded ? RuntimeStatusMessage(currentLifecycle) : result.Message,
            result.Succeeded && IsRuntimeTransitionPending(currentLifecycle) ? hostId : null
        );
    }

    private async Task<AdminHostOperationResult> CreateHostAsync(
        UserIdentity user,
        CancellationToken ct
    )
    {
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
        return new AdminHostOperationResult(true, $"Added a channel for {displayName}.");
    }

    private static string RuntimeStatusMessage(HostedChannelRuntimeLifecycle? lifecycle)
    {
        return lifecycle?.Match(
                static _ => "Bot offline.",
                static _ => "Bot starting.",
                static _ => "Bot running.",
                static _ => "Bot stopping."
            ) ?? "Bot offline.";
    }

    private static bool IsRuntimeTransitionPending(HostedChannelRuntimeLifecycle? lifecycle)
    {
        return lifecycle?.Match(
                static _ => false,
                static _ => true,
                static _ => false,
                static _ => true
            ) ?? false;
    }
}

public sealed record AdminHostOperationResult(
    bool Succeeded,
    string Message,
    int? PendingRuntimeHostId = null
);
