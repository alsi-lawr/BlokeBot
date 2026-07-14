using BlokeBot.Features.HostedChannels.Runtime;

namespace BlokeBot.Features.Admin.HostedChannels;

public sealed record HostedChannelAdminView(
    int Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsChannelBotAuthorized,
    HostedChannelRuntimeLifecycle Lifecycle
);
