using BlokeBot.Twitch.Runtime;

namespace BlokeBot.Features.CustomCommands;

public interface ICustomAnnouncementSender
{
    bool IsEnabled { get; }

    Task SendAsync(string channel, string message, CancellationToken ct);
}

internal sealed class DisabledCustomAnnouncementSender : ICustomAnnouncementSender
{
    public bool IsEnabled => false;

    public Task SendAsync(string channel, string message, CancellationToken ct) =>
        Task.CompletedTask;
}

internal sealed class TwitchCustomAnnouncementSender(ITwitchChatMessageSender sender)
    : ICustomAnnouncementSender
{
    public bool IsEnabled => true;

    public Task SendAsync(string channel, string message, CancellationToken ct) =>
        sender.SendAsync(channel, message, ct);
}
