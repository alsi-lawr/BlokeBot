using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.Polls.Page;

public partial class PollsPage
{
    private static readonly StudioSegmentedOption<int>[] _durationOptions =
    [
        new(30, "30s"),
        new(60, "60s"),
        new(120, "2 min"),
        new(300, "5 min"),
        new(0, "Custom"),
    ];

    private readonly HashSet<PollStage> _openStages = [];
    private readonly List<string> _choices = ["", ""];
    private string _title = string.Empty;
    private string _duration = "60";
    private int _durationChoice = 60;
    private bool _channelPointsVotingEnabled;
    private bool _stagesSeeded;
    private string _channelPointsPerVote = string.Empty;

    private enum PollStage
    {
        Template,
        Results,
    }

    protected override HostFeatureFlags Feature => HostFeatureFlags.Polls;

    protected override async Task<PollDashboardState?> LoadStateAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var state = await _polls.LoadAsync(hostId, cancellationToken);
        if (!_stagesSeeded && state is not null)
        {
            _stagesSeeded = true;
            if (state.Templates.Count == 0)
            {
                _ = _openStages.Add(PollStage.Template);
            }
        }

        return state;
    }

    private bool IsStageOpen(PollStage stage) => _openStages.Contains(stage);

    private void SetStage(PollStage stage, bool open) =>
        _ = open ? _openStages.Add(stage) : _openStages.Remove(stage);

    private string _editorSummary
    {
        get
        {
            var filled = _choices.Count(static choice => !string.IsNullOrWhiteSpace(choice));
            return string.IsNullOrWhiteSpace(_title) && filled == 0
                ? "Question · 2–5 choices · duration · optional Points voting"
                : $"“{(string.IsNullOrWhiteSpace(_title) ? "Untitled" : _title.Trim())}” · {filled} choices"
                    + $" · {DurationProse.Format(int.TryParse(_duration, out var seconds) ? seconds : 0)}"
                    + (_channelPointsVotingEnabled ? " · Channel Points voting" : string.Empty);
        }
    }

    private string _resultsSummary =>
        State is not { Results.Count: > 0 } state
            ? "No poll results yet"
            : $"{state.Results.Count} finished · latest: “{state.Results[0].Title}”";

    private void SetDurationChoice(int choice)
    {
        _durationChoice = choice;
        if (choice > 0)
        {
            _duration = choice.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string TemplateSummary(PollTemplateView template) =>
        string.Join(" · ", template.Choices)
        + $" · {DurationProse.Format(template.DurationSeconds)}"
        + (
            template is { ChannelPointsVotingEnabled: true, ChannelPointsPerVote: { } cost }
                ? $" · Channel Points voting, {cost:N0} per vote"
                : string.Empty
        );

    private static string ResultProse(PollView result)
    {
        var total = result.Choices.Sum(static choice => choice.Votes);
        return string.Join(
            " · ",
            result.Choices.Select(choice =>
                $"{choice.Title} {Percent(choice.Votes, total)}% ({choice.Votes})"
            )
        );
    }

    private static string ResultWhen(PollView result) =>
        result.EndedAtUtc is { } ended
            ? $"{result.Status} · {ended.ToLocalTime():MMM d, HH:mm}"
            : result.Status;

    private static int Percent(int votes, int totalVotes) =>
        totalVotes == 0 ? 0 : (int)Math.Round(votes * 100.0 / totalVotes);

    private IReadOnlyList<string> PreviewChoices() =>
        _choices
            .Select(static choice => choice.Trim())
            .Where(static choice => choice.Length > 0)
            .ToArray();

    private string PreviewKicker()
    {
        var duration = int.TryParse(_duration, out var seconds) ? seconds : 0;
        var kicker = $"Poll · {DurationProse.Format(duration)}";
        return _channelPointsVotingEnabled && int.TryParse(_channelPointsPerVote, out var cost)
            ? $"{kicker} · {cost:N0} points per extra vote"
            : kicker;
    }

    // Illustrative sample splits, labelled as sample votes in the preview heading.
    private static int SamplePercent(int index, int count) =>
        (count, index) switch
        {
            (2, _) => index == 0 ? 62 : 38,
            (3, _) => index switch
            {
                0 => 62,
                1 => 27,
                _ => 11,
            },
            (4, _) => index switch
            {
                0 => 46,
                1 => 27,
                2 => 16,
                _ => 11,
            },
            _ => index switch
            {
                0 => 39,
                1 => 24,
                2 => 16,
                3 => 12,
                _ => 9,
            },
        };

    private async Task SaveTemplateAsync()
    {
        if (!int.TryParse(_duration, out var duration))
        {
            Warn("Poll duration must be a number.");
            return;
        }
        int? pointsPerVote = null;
        if (_channelPointsVotingEnabled)
        {
            if (!int.TryParse(_channelPointsPerVote, out var parsed))
            {
                Warn("Channel Points cost must be a whole number from 1 to 1,000,000.");
                return;
            }

            pointsPerVote = parsed;
        }

        await MutateAsync(async hostId =>
        {
            var outcome = await _polls.SaveTemplateAsync(
                hostId,
                new(
                    _title,
                    _choices.ToArray(),
                    duration,
                    _channelPointsVotingEnabled,
                    pointsPerVote
                ),
                CancellationToken.None
            );
            Publish(outcome);
        });
    }

    private Task StartPollAsync(int templateId) =>
        MutateAsync(async hostId =>
            Publish(await _polls.StartAsync(hostId, templateId, CancellationToken.None))
        );

    private async Task EndPollAsync()
    {
        var confirmed =
            State?.ActivePoll?.IsExternallyStarted != true
            || await _js.InvokeAsync<bool>("confirm", ["End the externally started Twitch poll?"]);
        await MutateAsync(async hostId =>
            Publish(await _polls.EndAsync(hostId, confirmed, CancellationToken.None))
        );
    }

    private void Publish(PollOperationOutcome outcome)
    {
        var (message, success) = outcome switch
        {
            PollOperationOutcome.Started => ("Poll started.", true),
            PollOperationOutcome.Ended => ("Poll ended.", true),
            PollOperationOutcome.TemplateSaved => ("Poll template saved.", true),
            PollOperationOutcome.ActivePollExists => ("Twitch already has an active poll.", false),
            PollOperationOutcome.TemplateNotFound => (
                "That poll template is no longer available.",
                false
            ),
            PollOperationOutcome.ConfirmationRequired => (
                "Confirm before ending a poll started outside BlokeBot.",
                false
            ),
            PollOperationOutcome.NotReady => (
                "Reconnect this channel to Twitch, then try again.",
                false
            ),
            PollOperationOutcome.InvalidTemplate invalid => (invalid.Message, false),
            PollOperationOutcome.ProviderRejected => (
                "Twitch could not complete that poll action. Reload the page before trying again.",
                false
            ),
            _ => throw new UnreachableException(),
        };
        Publish(message, success);
    }
}
