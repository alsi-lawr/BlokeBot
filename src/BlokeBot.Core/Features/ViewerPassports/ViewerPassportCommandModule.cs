using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPassports;

internal sealed class ViewerPassportCommandModule(
    ViewerPassportService passports,
    PublicSiteLinks links
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands) =>
        _ = commands.MapContextual(FixedChatCommandRoutes.Passport, PassportAsync);

    private async ValueTask<CommandHandlingOutcome> PassportAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var hasViewerId =
            context.Message.Tags.TryGetValue("user-id", out var twitchUserId)
            && !string.IsNullOrWhiteSpace(twitchUserId);
        var outcome = hasViewerId
            ? await passports.GetVisibleByIdentityAsync(
                context.Message.Channel,
                new(
                    twitchUserId!,
                    context.Message.Login,
                    context.Message.Tags.GetValueOrDefault("display-name", context.Message.Login)
                ),
                new(twitchUserId!, false),
                cancellationToken
            )
            : await passports.GetVisibleAsync(
                context.Message.Channel,
                context.Message.Login,
                new(context.Message.Login, false),
                cancellationToken
            );
        if (outcome is ViewerPassportQueryOutcome.FeatureDisabled)
        {
            return new CommandHandlingOutcome.Unhandled();
        }
        if (args.Count > 0 || !hasViewerId)
        {
            return new CommandHandlingOutcome.Handled();
        }
        if (outcome is not ViewerPassportQueryOutcome.Available { Passport: var passport })
        {
            return new CommandHandlingOutcome.Handled();
        }
        if (passport.Visibility != ViewerPassportVisibility.Public)
        {
            await context.ReplyAsync(
                $"Open your viewer passport: {links.Resolve($"/passports/{Uri.EscapeDataString(passport.HostLogin)}/me")}",
                cancellationToken
            );
            return new CommandHandlingOutcome.Handled();
        }

        var attendance = passport.HideAttendance
            ? string.Empty
            : $", {passport.Statistics.AttendanceStreakSessions}-stream attendance streak";
        await context.ReplyAsync(
            $"{passport.DisplayName}: {passport.Statistics.Points} points"
                + (passport.Statistics.PointsRank is { } rank ? $" (#{rank})" : string.Empty)
                + $", {passport.Statistics.CorrectGuesses}/{passport.Statistics.GuessRounds} guesses correct"
                + $", {passport.Statistics.Achievements} achievements{attendance}. "
                + links.Resolve(
                    $"/passport/{Uri.EscapeDataString(passport.HostLogin)}/{Uri.EscapeDataString(passport.Login)}"
                ),
            cancellationToken
        );
        return new CommandHandlingOutcome.Handled();
    }
}
