using System.Diagnostics;
using BlokeBot.Core.Auth.Users;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.Admin.HostedChannels;

internal sealed class AdminHostManagementService(
    UserLookupService users,
    BotHostProvisioningService hostProvisioning,
    BotHostRemovalService hostRemoval,
    HostedChannelRuntimeControlService runtime,
    HostedChannelDirectoryService hostedChannels
)
{
    public IO<AdminHostOperationOutcome, AdminHostOperationError> CreateHost(string login) =>
        IO<AdminHostOperationOutcome, AdminHostOperationError>.Create(async ct =>
        {
            Result<Option<UserIdentity>, AccessTokenUnavailableReason> user;
            try
            {
                user = await users.FindByLogin(login).ExecuteAsync(ct);
            }
            catch (HttpRequestException exception)
            {
                ct.ThrowIfCancellationRequested();
                return Error(new AdminHostOperationError.LookupUnavailable(exception));
            }

            return await user.Match(
                found =>
                    found.Match(
                        identity => CreateHostAsync(identity, ct),
                        () =>
                            Task.FromResult(
                                Success(
                                    new AdminHostOperationOutcome.Rejected("Twitch user not found.")
                                )
                            )
                    ),
                reason =>
                    Task.FromResult(Error(new AdminHostOperationError.BotTokenUnavailable(reason)))
            );
        });

    public IO<AdminHostOperationOutcome, AdminHostOperationError> RemoveHost(int hostId) =>
        IO<AdminHostOperationOutcome, AdminHostOperationError>.Create(async ct =>
        {
            _ = await runtime.Stop(hostId).ExecuteAsync(ct);
            var result = await hostRemoval.RemoveAsync(hostId, ct);
            var message = result.Removed ? "Channel removed." : "Channel was already removed.";
            AdminHostOperationOutcome outcome = result.Media is HostMediaCleanup.Failed failed
                ? new AdminHostOperationOutcome.Rejected(
                    $"{message} Its overlay media could not be fully deleted; "
                        + $"remove {failed.Directory} from the server manually."
                )
                : new AdminHostOperationOutcome.Completed(message);
            return Success(outcome);
        });

    public IO<AdminHostOperationOutcome, AdminHostOperationError> StartBot(int hostId) =>
        ApplyRuntimeOperation(hostId, runtime.Start(hostId));

    public IO<AdminHostOperationOutcome, AdminHostOperationError> StopBot(int hostId) =>
        ApplyRuntimeOperation(hostId, runtime.Stop(hostId));

    public AdminHostOperationOutcome RefreshPendingRuntime(
        int hostId,
        IReadOnlyList<HostedChannelAdminView> hosts
    )
    {
        var lifecycle = hosts.FirstOrDefault(x => x.Id == hostId)?.Lifecycle;
        var message = RuntimeStatusMessage(lifecycle);
        return IsRuntimeTransitionPending(lifecycle)
            ? new AdminHostOperationOutcome.PendingRuntime(message, hostId)
            : new AdminHostOperationOutcome.Completed(message);
    }

    private IO<AdminHostOperationOutcome, AdminHostOperationError> ApplyRuntimeOperation(
        int hostId,
        IO<HostedChannelRuntimeControlOutcome, Never> operation
    ) =>
        IO<AdminHostOperationOutcome, AdminHostOperationError>.Create(async ct =>
        {
            var result = await operation.ExecuteAsync(ct);
            var outcome = result.Match(value => value, _ => throw new UnreachableException());
            if (outcome is not HostedChannelRuntimeControlOutcome.Accepted)
            {
                return Success(
                    new AdminHostOperationOutcome.Rejected(RuntimeFailureMessage(outcome))
                );
            }

            var hosts = await hostedChannels.LoadHostedChannelsAsync(ct);
            var lifecycle = hosts.FirstOrDefault(host => host.Id == hostId)?.Lifecycle;
            var message = RuntimeStatusMessage(lifecycle);
            return Success(
                IsRuntimeTransitionPending(lifecycle)
                    ? new AdminHostOperationOutcome.PendingRuntime(message, hostId)
                    : new AdminHostOperationOutcome.Completed(message)
            );
        });

    private async Task<Result<AdminHostOperationOutcome, AdminHostOperationError>> CreateHostAsync(
        UserIdentity user,
        CancellationToken ct
    )
    {
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Login
            : user.DisplayName;
        _ = await hostProvisioning.EnsureHostAsync(
            user.Login,
            user.Id,
            displayName,
            user.ProfileImageUrl,
            ct
        );
        return Success(
            new AdminHostOperationOutcome.Completed($"Added a channel for {displayName}.")
        );
    }

    private static string RuntimeFailureMessage(HostedChannelRuntimeControlOutcome outcome) =>
        outcome switch
        {
            HostedChannelRuntimeControlOutcome.HostNotFound => "Channel setup was not found.",
            HostedChannelRuntimeControlOutcome.ChannelAuthorizationRequired =>
                "Connect the bot to Twitch chat before starting it.",
            HostedChannelRuntimeControlOutcome.CustomBotNotReady =>
                "Connect the custom bot account before starting it, or turn custom bot off.",
            HostedChannelRuntimeControlOutcome.Cooldown cooldown =>
                $"Wait until {cooldown.NextAllowedAtUtc.ToLocalTime():HH:mm:ss} before starting or stopping the bot again.",
            _ => throw new UnreachableException(),
        };

    private static string RuntimeStatusMessage(HostedChannelRuntimeLifecycle? lifecycle) =>
        lifecycle?.Match(
            static _ => "Bot offline.",
            static _ => "Bot starting.",
            static _ => "Bot running.",
            static _ => "Bot stopping."
        ) ?? "Bot offline.";

    private static bool IsRuntimeTransitionPending(HostedChannelRuntimeLifecycle? lifecycle) =>
        lifecycle?.Match(static _ => false, static _ => true, static _ => false, static _ => true)
        ?? false;

    private static Result<AdminHostOperationOutcome, AdminHostOperationError> Success(
        AdminHostOperationOutcome outcome
    ) => Result<AdminHostOperationOutcome, AdminHostOperationError>.Success(outcome);

    private static Result<AdminHostOperationOutcome, AdminHostOperationError> Error(
        AdminHostOperationError error
    ) => Result<AdminHostOperationOutcome, AdminHostOperationError>.Error(error);
}

public abstract record AdminHostOperationOutcome
{
    private AdminHostOperationOutcome() { }

    public sealed record Completed(string Message) : AdminHostOperationOutcome;

    public sealed record PendingRuntime(string Message, int HostId) : AdminHostOperationOutcome;

    public sealed record Rejected(string Message) : AdminHostOperationOutcome;
}

public abstract record AdminHostOperationError
{
    private AdminHostOperationError() { }

    public sealed record LookupUnavailable(HttpRequestException Cause) : AdminHostOperationError;

    public sealed record BotTokenUnavailable(AccessTokenUnavailableReason Reason)
        : AdminHostOperationError;
}
