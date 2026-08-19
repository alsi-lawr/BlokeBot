using BlokeBot.Core.Components.Layout;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.ViewerPassports;

internal sealed class ViewerPassportCommandModule(
    ViewerPassportService passports,
    IOptions<BlokeBotOptions> options
) : IChatCommandModule
{
    /// <summary>
    /// Builds the address a viewer can open. A deployment that configures
    /// <c>BlokeBot:PublicBaseUrl</c> gets a full link; otherwise the reply keeps the bare path.
    /// </summary>
    private string Link(string path) =>
        HelpSiteGuide.BaseAddress(options.Value.PublicBaseUrl) is { } baseAddress
            ? new Uri(baseAddress, path.TrimStart('/')).ToString()
            : path;

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
        var outcome = await passports.GetVisibleByIdentityAsync(
            context.Message.Channel,
            new(
                twitchUserId,
                context.Message.Login,
                context.Message.Tags.GetValueOrDefault("display-name", context.Message.Login)
            ),
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
                $"Open your viewer passport: {Link($"/passports/{Uri.EscapeDataString(passport.HostLogin)}/me")}",
                cancellationToken
            );
            return;
        }

        var attendance = passport.HideAttendance
            ? string.Empty
            : $", {passport.Statistics.AttendanceStreakSessions}-stream attendance streak";
        await context.ReplyAsync(
            $"{passport.DisplayName}: {passport.Statistics.Points} points"
                + (passport.Statistics.PointsRank is { } rank ? $" (#{rank})" : string.Empty)
                + $", {passport.Statistics.CorrectGuesses}/{passport.Statistics.GuessRounds} guesses correct"
                + $", {passport.Statistics.Achievements} achievements{attendance}. "
                + Link(
                    $"/passport/{Uri.EscapeDataString(passport.HostLogin)}/{Uri.EscapeDataString(passport.Login)}"
                ),
            cancellationToken
        );
    }
}
