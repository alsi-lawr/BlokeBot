using System.Security.Cryptography;
using System.Text;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.BlokeRaid;

internal sealed class BlokeRaidCommandModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BlokeRaidService raids,
    IHostStreamLivenessProvider streams
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands) =>
        commands.Map(FixedChatCommandRoutes.Raid, ExecuteAsync);

    private async ValueTask ExecuteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var host = await FindEnabledHostAsync(context.Message.Channel, cancellationToken);
        if (host is null)
        {
            return;
        }

        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "status";
        switch (action)
        {
            case "attack":
                await ActAsync(host, context, BlokeRaidActionKind.Attack, cancellationToken);
                break;
            case "mend":
            case "heal":
            case "defend":
                await ActAsync(host, context, BlokeRaidActionKind.Mend, cancellationToken);
                break;
            case "nova":
            case "special":
                await ActAsync(host, context, BlokeRaidActionKind.Special, cancellationToken);
                break;
            case "standings":
                await StandingsAsync(host, context, cancellationToken);
                break;
            case "start":
                await ModeratorCampaignAsync(host, context, "start", cancellationToken);
                break;
            case "end":
                await ModeratorCampaignAsync(host, context, "end", cancellationToken);
                break;
            case "reset":
                await ModeratorCampaignAsync(host, context, "reset", cancellationToken);
                break;
            case "status":
                await StatusAsync(host, context, cancellationToken);
                break;
            default:
                await context.ReplyAsync(
                    "Usage: !raid [status|attack|mend|nova|standings]. Moderators: start, end, reset.",
                    cancellationToken
                );
                break;
        }
    }

    private async Task ActAsync(
        HostIdentity host,
        ChatCommandContext context,
        BlokeRaidActionKind kind,
        CancellationToken cancellationToken
    )
    {
        var livenessResult = await streams
            .GetStreamLiveness(host.Login)
            .ExecuteAsync(cancellationToken);
        var liveness = livenessResult.Match(
            value => value,
            _ => new HostStreamLivenessOutcome.Offline()
        );
        if (liveness is not HostStreamLivenessOutcome.Live live)
        {
            await context.ReplyAsync(
                liveness is HostStreamLivenessOutcome.Offline
                    ? "BlokeRaid actions are available while the stream is live."
                    : "BlokeRaid could not confirm the current stream. Try again shortly.",
                cancellationToken
            );
            return;
        }

        var viewer = Viewer(context.Message);
        var outcome = await raids.ActAsync(
            host.Id,
            new(OperationKey(context.Message), kind, viewer, live.StreamId),
            cancellationToken
        );
        await context.ReplyAsync(ActionMessage(outcome, viewer.Login), cancellationToken);
    }

    private async Task StatusAsync(
        HostIdentity host,
        ChatCommandContext context,
        CancellationToken cancellationToken
    )
    {
        var view = await raids.LoadPublicAsync(host.Login, cancellationToken);
        if (view?.ActiveCampaign is not { } campaign)
        {
            await context.ReplyAsync("No BlokeRaid boss is active.", cancellationToken);
            return;
        }
        await context.ReplyAsync(
            $"BlokeRaid: {campaign.BossName} has {campaign.CurrentHealth:N0}/{campaign.MaximumHealth:N0} health in phase {campaign.CurrentPhase}. !raid attack | mend | nova · /raid/{host.Login}",
            cancellationToken
        );
    }

    private async Task StandingsAsync(
        HostIdentity host,
        ChatCommandContext context,
        CancellationToken cancellationToken
    )
    {
        var view = await raids.LoadPublicAsync(host.Login, cancellationToken);
        var standings = view?.ActiveCampaign?.Contributions.Take(5).ToArray() ?? [];
        await context.ReplyAsync(
            standings.Length == 0
                ? "No BlokeRaid contributions have been recorded yet."
                : $"BlokeRaid leaders: {string.Join(", ", standings.Select((value, index) => $"{index + 1}. @{value.Viewer.Login} {value.Total:N0}"))}. /raid/{host.Login}",
            cancellationToken
        );
    }

    private async Task ModeratorCampaignAsync(
        HostIdentity host,
        ChatCommandContext context,
        string action,
        CancellationToken cancellationToken
    )
    {
        if (!ChatModeratorPolicy.IsModerator(context.Message))
        {
            await context.ReplyAsync(
                "That BlokeRaid command is moderator-only.",
                cancellationToken
            );
            return;
        }
        var command = new BlokeRaidCampaignCommand(
            OperationKey(context.Message),
            new(
                context.Message.Tags.GetValueOrDefault("user-id", context.Message.Login),
                CommunityInput.NormalizeLogin(context.Message.Login)
            ),
            "chat command"
        );
        var outcome = action switch
        {
            "start" => await raids.StartAsync(host.Id, command, cancellationToken),
            "end" => await raids.EndAsync(host.Id, command, cancellationToken),
            "reset" => await raids.ResetAsync(host.Id, command, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported moderator raid action."),
        };
        await context.ReplyAsync(CampaignMessage(outcome), cancellationToken);
    }

    private async Task<HostIdentity?> FindEnabledHostAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        var login = CommunityInput.NormalizeLogin(channel);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Login == login
                && (value.EnabledFeatures & HostFeatureFlags.CooperativeGame)
                    == HostFeatureFlags.CooperativeGame
            )
            .Select(value => new HostIdentity(value.Id, value.Login))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static BlokeRaidViewer Viewer(ChatMessage message)
    {
        var login = CommunityInput.NormalizeLogin(message.Login);
        return new(
            message.Tags.GetValueOrDefault("user-id", $"login:{login}"),
            login,
            message.Tags.GetValueOrDefault("display-name", message.Login)
        );
    }

    private static string ActionMessage(BlokeRaidActionOutcome outcome, string login) =>
        outcome switch
        {
            BlokeRaidActionOutcome.Succeeded succeeded =>
                $"@{login} {succeeded.Action.Response} {succeeded.Campaign.BossName}: {succeeded.Campaign.CurrentHealth:N0}/{succeeded.Campaign.MaximumHealth:N0} health, ward {succeeded.Campaign.CurrentWard:N0}/{succeeded.Campaign.MaximumWard:N0}.",
            BlokeRaidActionOutcome.NoActiveCampaign => "No BlokeRaid boss is active.",
            BlokeRaidActionOutcome.Cooldown cooldown =>
                $"@{login}, that raid action is ready in {Math.Ceiling(cooldown.Remaining.TotalSeconds):N0}s.",
            BlokeRaidActionOutcome.PerStreamLimitReached =>
                $"@{login}, you reached this action's limit for the current stream.",
            BlokeRaidActionOutcome.InsufficientPoints insufficient =>
                $"@{login}, Nova costs {insufficient.Cost.ToDisplayString()} points; your balance is {insufficient.Balance.ToDisplayString()}.",
            BlokeRaidActionOutcome.PointCapacityExceeded =>
                "The victory reward could not fit a contributor's point balance. A moderator needs to review Points.",
            BlokeRaidActionOutcome.Invalid invalid => invalid.Message,
            BlokeRaidActionOutcome.FeatureDisabled or BlokeRaidActionOutcome.SourceSuppressed =>
                "BlokeRaid is unavailable.",
            _ => "BlokeRaid could not resolve that action.",
        };

    private static string CampaignMessage(BlokeRaidCampaignOutcome outcome) =>
        outcome switch
        {
            BlokeRaidCampaignOutcome.Succeeded succeeded =>
                $"BlokeRaid {succeeded.Campaign.BossName} is {succeeded.Campaign.Status}: {succeeded.Campaign.CurrentHealth:N0}/{succeeded.Campaign.MaximumHealth:N0} health.",
            BlokeRaidCampaignOutcome.NoActiveCampaign => "No BlokeRaid boss is active.",
            BlokeRaidCampaignOutcome.Conflict conflict => conflict.Message,
            BlokeRaidCampaignOutcome.Invalid invalid => invalid.Message,
            BlokeRaidCampaignOutcome.FeatureDisabled => "BlokeRaid is unavailable.",
            _ => "BlokeRaid could not complete that command.",
        };

    private static string OperationKey(ChatMessage message)
    {
        var messageId = message.Tags.GetValueOrDefault("id", string.Empty);
        return string.IsNullOrWhiteSpace(messageId)
            ? $"chat:{Guid.NewGuid():N}"
            : $"chat:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(messageId))).ToLowerInvariant()}";
    }

    private sealed record HostIdentity(int Id, string Login);
}
