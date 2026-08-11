using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPassports;

internal sealed class ViewerPassportCommandModule(ViewerPassportService passports)
    : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands) =>
        _ = commands.Map(FixedChatCommandRoutes.Passport, PassportAsync);

    private async ValueTask PassportAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        if (
            args.Count > 0
            || !context.Message.Tags.TryGetValue("user-id", out var twitchUserId)
            || string.IsNullOrWhiteSpace(twitchUserId)
        )
        {
            return;
        }
        var outcome = await passports.GetVisibleAsync(
            context.Message.Channel,
            context.Message.Login,
            new(twitchUserId, false),
            cancellationToken
        );
        if (outcome is not ViewerPassportQueryOutcome.Available { Passport: var passport })
        {
            return;
        }
        if (passport.Visibility != ViewerPassportVisibility.Public)
        {
            await context.ReplyAsync(
                $"Open your viewer passport: /passports/{passport.HostLogin}/me",
                cancellationToken
            );
            return;
        }

        var attendance = passport.HideAttendance
            ? string.Empty
            : $", {passport.Statistics.AttendanceStreakDays}-day chat-presence streak";
        await context.ReplyAsync(
            $"{passport.DisplayName}: {passport.Statistics.Points} points"
                + (passport.Statistics.PointsRank is { } rank ? $" (#{rank})" : string.Empty)
                + $", {passport.Statistics.CorrectGuesses}/{passport.Statistics.GuessRounds} guesses correct"
                + $", {passport.Statistics.Achievements} achievements{attendance}. "
                + $"/passport/{passport.HostLogin}/{passport.Login}",
            cancellationToken
        );
    }
}
