using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostConfig.StartupMessage;

public sealed class StartupMessageConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BotSettings botSettings
) : IStartupChatMessageProvider
{
    public async Task<StartupMessageSaveOutcome> SaveAsync(
        AuthenticatedSession session,
        int hostId,
        StartupMessageSaveCommand command,
        CancellationToken cancellationToken
    )
    {
        if (!CanConfigure(session, hostId))
        {
            return new StartupMessageSaveOutcome.Unauthorized();
        }

        var normalizedText = command.Text.Trim();
        if (command.Enabled && normalizedText.Length == 0)
        {
            return new StartupMessageSaveOutcome.TextRequired();
        }

        if (
            command.Enabled
            && botSettings.MaxChatMessageLength > 0
            && normalizedText.Length > botSettings.MaxChatMessageLength
        )
        {
            return new StartupMessageSaveOutcome.TextTooLong(botSettings.MaxChatMessageLength);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
        if (host is null)
        {
            return new StartupMessageSaveOutcome.HostNotFound();
        }

        host.StartupMessageEnabled = command.Enabled;
        host.StartupMessageText = normalizedText.Length == 0 ? null : normalizedText;
        await db.SaveChangesAsync(cancellationToken);
        return new StartupMessageSaveOutcome.Saved(
            new StartupMessageConfiguration(command.Enabled, normalizedText)
        );
    }

    public async ValueTask<StartupChatMessage> GetAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        var login = channel.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Login == login, cancellationToken);
        return host is null ? ConfiguredDefault() : EffectiveRuntimeMessage(host);
    }

    internal StartupMessageConfiguration EffectiveConfiguration(BotHost host)
    {
        if (host.StartupMessageEnabled is null)
        {
            var fallback = botSettings.StartupMessage.Trim();
            return new(fallback.Length > 0, fallback);
        }

        var text = host.StartupMessageText?.Trim() ?? string.Empty;
        return new(host.StartupMessageEnabled.Value && text.Length > 0, text);
    }

    private StartupChatMessage EffectiveRuntimeMessage(BotHost host)
    {
        var configuration = EffectiveConfiguration(host);
        return configuration.Enabled
            ? new StartupChatMessage.Enabled(configuration.Text)
            : new StartupChatMessage.Disabled();
    }

    private StartupChatMessage ConfiguredDefault()
    {
        return string.IsNullOrWhiteSpace(botSettings.StartupMessage)
            ? new StartupChatMessage.Disabled()
            : new StartupChatMessage.Enabled(botSettings.StartupMessage.Trim());
    }

    private static bool CanConfigure(AuthenticatedSession session, int hostId)
    {
        var selectedHost = session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
        return selectedHost?.Id == hostId && session.CanManageSelectedHostConfig;
    }
}
