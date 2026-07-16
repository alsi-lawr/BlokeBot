using BlokeBot.Core.Features.HostedChannels.Runtime;

namespace BlokeBot.Core.Features.Admin.HostedChannels;

public sealed record HostedChannelAdminView(
    int Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsChannelBotAuthorized,
    HostedChannelRuntimeLifecycle Lifecycle
);
