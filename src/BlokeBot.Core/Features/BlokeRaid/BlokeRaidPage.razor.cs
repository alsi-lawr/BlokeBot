using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.BlokeRaid;

public partial class BlokeRaidPage
{
    private const string _workspaceTabsId = "raid-workspace";
    private const string _campaignKey = "campaign";
    private const string _configurationKey = "configuration";

    private static readonly IReadOnlyList<SegmentedTabItem> _workspaceTabs =
    [
        new(_campaignKey, "Campaign"),
        new(_configurationKey, "Configuration"),
    ];

    private static readonly IReadOnlyList<ResetPolicyOption> _resetPolicies =
    [
        new(
            BlokeRaidResetPolicy.Manual,
            "raid-reset-manual",
            "↻",
            "Manual",
            "A fresh campaign starts only when a moderator resets it here."
        ),
        new(
            BlokeRaidResetPolicy.Weekly,
            "raid-reset-weekly",
            "7",
            "Weekly",
            "At the chosen day and hour the active campaign ends and a fresh one starts."
        ),
    ];

    [Inject]
    private NavigationManager _navigation { get; set; } = null!;

    private BlokeRaidModeratorView? _view;
    private ConfigurationEditor _editor = new();
    private string _workspaceKey = _campaignKey;
    private bool _enabled;
    private bool _loaded;
    private bool _operationFailed;
    private bool _saving;
    private string _feedback = string.Empty;
    private PageSaveFeedback? _saveFeedback;

    private string _publicUrl => $"/raid/{Uri.EscapeDataString(HostLogin)}";

    protected override async Task OnInitializedAsync()
    {
        _workspaceKey = SegmentedTabs.CanonicalKey(_navigation, _workspaceTabs);
        _ = await LoadPageContextAsync();
        await LoadAsync(adoptConfiguration: true);
    }

    private static string DescriptionFor(string key) =>
        key == _configurationKey
            ? "Set the rules for the current and next campaign."
            : "Lead chat through one persistent channel boss campaign.";

    private void SelectWorkspace(string key) => _workspaceKey = key;

    private void SelectResetPolicy(BlokeRaidResetPolicy policy) => _editor.ResetPolicy = policy;

    private string ResetOptionClass(BlokeRaidResetPolicy policy) =>
        _editor.ResetPolicy == policy ? "raid-option raid-option--selected" : "raid-option";

    private static string RuleRange(ActionRuleEditor rule) =>
        $"{rule.Minimum:N0}–{rule.Maximum:N0}";

    private string PhaseOneBand() =>
        $"100–{Math.Clamp(_editor.PhaseTwoHealthPercent + 1, 0, 100)}%";

    private string PhaseTwoBand() =>
        $"{_editor.PhaseTwoHealthPercent}–{Math.Clamp(_editor.PhaseThreeHealthPercent + 1, 0, 100)}%";

    private string PhaseThreeBand() => $"{_editor.PhaseThreeHealthPercent}% and below";

    private async Task LoadAsync(bool adoptConfiguration = false)
    {
        if (HostId == 0)
        {
            _loaded = true;
            return;
        }
        _enabled = await _features.IsEnabledAsync(
            HostId,
            HostFeatureFlags.CooperativeGame,
            CancellationToken.None
        );
        _view = _enabled ? await _raids.LoadModeratorAsync(HostId, CancellationToken.None) : null;
        if (adoptConfiguration && _view is not null)
        {
            _editor = ConfigurationEditor.From(_view.Configuration);
        }
        _loaded = true;
    }

    private async Task StartAsync() =>
        await MutateCampaignAsync(command =>
            _raids.StartAsync(HostId, command, CancellationToken.None)
        );

    private async Task EndAsync() =>
        await MutateCampaignAsync(command =>
            _raids.EndAsync(HostId, command, CancellationToken.None)
        );

    private async Task ResetAsync() =>
        await MutateCampaignAsync(command =>
            _raids.ResetAsync(HostId, command, CancellationToken.None)
        );

    private async Task MutateCampaignAsync(
        Func<BlokeRaidCampaignCommand, Task<BlokeRaidCampaignOutcome>> mutation
    ) =>
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var outcome = await mutation(
                    new(
                        $"ui:{Guid.NewGuid():N}",
                        new(PageContext.Session.UserId, ActorLogin),
                        "moderator dashboard"
                    )
                );
                _feedback = CampaignMessage(outcome);
                _operationFailed = outcome is not BlokeRaidCampaignOutcome.Succeeded;
                await LoadAsync();
            }
        );

    private async Task SaveConfigurationAsync()
    {
        if (!TryAmount(_editor.SpecialPointCost, out var specialCost))
        {
            SaveFailed("Special point cost must be a non-negative whole number.");
            return;
        }
        if (!TryAmount(_editor.VictoryPointReward, out var victoryReward))
        {
            SaveFailed("Victory reward must be a non-negative whole number.");
            return;
        }
        var draft = _editor.ToDraft(specialCost, victoryReward);
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                _saving = true;
                _saveFeedback = new("Saving configuration…", PageSaveFeedbackKind.Saving);
                try
                {
                    var outcome = await _raids.SaveConfigurationAsync(
                        HostId,
                        draft,
                        CancellationToken.None
                    );
                    if (outcome is BlokeRaidConfigurationOutcome.Saved saved)
                    {
                        _editor = ConfigurationEditor.From(saved.Configuration);
                        _saveFeedback = new(
                            "Campaign configuration saved.",
                            PageSaveFeedbackKind.Success
                        );
                        await LoadAsync();
                    }
                    else
                    {
                        SaveFailed(ConfigurationMessage(outcome));
                    }
                }
                finally
                {
                    _saving = false;
                }
            }
        );
    }

    private void SaveFailed(string message)
    {
        _saveFeedback = new(message, PageSaveFeedbackKind.Validation);
        _feedback = message;
        _operationFailed = true;
    }

    private string ResetSummary() =>
        _view?.Configuration.ResetPolicy == BlokeRaidResetPolicy.Weekly
            ? $"Weekly · {_view.Configuration.WeeklyResetDay} {_view.Configuration.WeeklyResetHourUtc:00}:00 UTC"
            : "Manual";

    private string PhaseResponse(int phase) =>
        phase switch
        {
            1 => _view!.Configuration.PhaseOneResponse,
            2 => _view!.Configuration.PhaseTwoResponse,
            _ => _view!.Configuration.PhaseThreeResponse,
        };

    private static string PhaseLabel(int phase) =>
        phase switch
        {
            1 => "The boss arrives",
            2 => "Fractured scales",
            _ => "Final stand",
        };

    private string Age(DateTime value)
    {
        var elapsed = _clock.GetUtcNow().UtcDateTime - value;
        return elapsed.TotalDays >= 1 ? $"{(int)elapsed.TotalDays}d {elapsed.Hours}h"
            : elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
            : elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes}m"
            : $"{Math.Max(0, (int)elapsed.TotalSeconds)}s";
    }

    private static int Percent(int value, int maximum) =>
        maximum == 0 ? 0 : (int)Math.Round(value * 100d / maximum);

    private static string Range(BlokeRaidActionRuleView rule) =>
        $"{rule.Minimum:N0}–{rule.Maximum:N0}";

    private static string Cooldown(BlokeRaidActionRuleView rule) =>
        rule.CooldownSeconds < 60 ? $"{rule.CooldownSeconds}s cooldown"
        : rule.CooldownSeconds % 60 == 0 ? $"{rule.CooldownSeconds / 60}m cooldown"
        : $"{rule.CooldownSeconds / 60}m {rule.CooldownSeconds % 60}s cooldown";

    private static string ActionIcon(BlokeRaidActionKind kind) =>
        kind switch
        {
            BlokeRaidActionKind.Attack => "⚔",
            BlokeRaidActionKind.Mend => "✚",
            BlokeRaidActionKind.Special => "✦",
            _ => "?",
        };

    private static string ActionHeading(BlokeRaidActionView action) =>
        action.Kind == BlokeRaidActionKind.CorrectGuess
            ? $"Correct guesses struck for {action.Outcome:N0}"
            : $"@{action.Viewer?.Login} {ActionVerb(action.Kind)} · {action.Outcome:N0}";

    private static string ActionDetail(BlokeRaidActionView action) =>
        action.Kind == BlokeRaidActionKind.Mend
            ? $"Ward {action.WardAfter:N0} · phase {action.PhaseAfter}"
        : action.Kind == BlokeRaidActionKind.CorrectGuess
            ? $"Round #{action.GuessRoundId} · health {action.BossHealthAfter:N0}"
        : $"Health {action.BossHealthAfter:N0} · phase {action.PhaseAfter}"
            + (
                action.PointCost.IsZero
                    ? string.Empty
                    : $" · {action.PointCost.ToDisplayString()} points spent"
            );

    private static string ActionVerb(BlokeRaidActionKind kind) =>
        kind switch
        {
            BlokeRaidActionKind.Attack => "attacked",
            BlokeRaidActionKind.Mend => "mended",
            BlokeRaidActionKind.Special => "cast Nova",
            _ => "acted",
        };

    private static string CampaignMessage(BlokeRaidCampaignOutcome outcome) =>
        outcome switch
        {
            BlokeRaidCampaignOutcome.Succeeded succeeded =>
                $"{succeeded.Campaign.BossName} is now {succeeded.Campaign.Status}.",
            BlokeRaidCampaignOutcome.NoActiveCampaign => "No BlokeRaid campaign is active.",
            BlokeRaidCampaignOutcome.Conflict conflict => conflict.Message,
            BlokeRaidCampaignOutcome.Invalid invalid => invalid.Message,
            BlokeRaidCampaignOutcome.FeatureDisabled => "Cooperative game is off for this channel.",
            _ => "BlokeRaid could not complete that operation.",
        };

    private static string ConfigurationMessage(BlokeRaidConfigurationOutcome outcome) =>
        outcome switch
        {
            BlokeRaidConfigurationOutcome.Conflict conflict => conflict.Message,
            BlokeRaidConfigurationOutcome.Invalid invalid => invalid.Message,
            BlokeRaidConfigurationOutcome.FeatureDisabled =>
                "Cooperative game is off for this channel.",
            _ => "BlokeRaid could not save the configuration.",
        };

    private static bool TryAmount(string value, out PointAmount amount)
    {
        PointAmount parsed = default;
        var valid = PointAmount
            .ParseNonNegativeAbsolute(value)
            .Match(
                result =>
                {
                    parsed = result;
                    return true;
                },
                _ => false
            );
        amount = parsed;
        return valid;
    }

    private sealed record ResetPolicyOption(
        BlokeRaidResetPolicy Policy,
        string Id,
        string Icon,
        string Title,
        string Description
    );

    private sealed class ConfigurationEditor
    {
        public int Revision { get; set; }
        public string BossName { get; set; } = string.Empty;
        public int MaximumHealth { get; set; }
        public int MaximumWard { get; set; }
        public int CampaignDurationHours { get; set; }
        public ActionRuleEditor Attack { get; set; } = new();
        public ActionRuleEditor Mend { get; set; } = new();
        public ActionRuleEditor Special { get; set; } = new();
        public int CorrectGuessDamage { get; set; }
        public string VictoryPointReward { get; set; } = "0";
        public int PhaseTwoHealthPercent { get; set; }
        public int PhaseThreeHealthPercent { get; set; }
        public string PhaseOneResponse { get; set; } = string.Empty;
        public string PhaseTwoResponse { get; set; } = string.Empty;
        public string PhaseThreeResponse { get; set; } = string.Empty;
        public string VictoryResponse { get; set; } = string.Empty;
        public string ExpiryResponse { get; set; } = string.Empty;
        public BlokeRaidResetPolicy ResetPolicy { get; set; }
        public DayOfWeek WeeklyResetDay { get; set; }
        public int WeeklyResetHourUtc { get; set; }
        public string SpecialPointCost
        {
            get => Special.PointCost;
            set => Special.PointCost = value;
        }

        public static ConfigurationEditor From(BlokeRaidConfigurationView value) =>
            new()
            {
                Revision = value.Revision,
                BossName = value.BossName,
                MaximumHealth = value.MaximumHealth,
                MaximumWard = value.MaximumWard,
                CampaignDurationHours = value.CampaignDurationHours,
                Attack = ActionRuleEditor.From(value.Attack),
                Mend = ActionRuleEditor.From(value.Mend),
                Special = ActionRuleEditor.From(value.Special),
                CorrectGuessDamage = value.CorrectGuessDamage,
                VictoryPointReward = value.VictoryPointReward.ToString(),
                PhaseTwoHealthPercent = value.PhaseTwoHealthPercent,
                PhaseThreeHealthPercent = value.PhaseThreeHealthPercent,
                PhaseOneResponse = value.PhaseOneResponse,
                PhaseTwoResponse = value.PhaseTwoResponse,
                PhaseThreeResponse = value.PhaseThreeResponse,
                VictoryResponse = value.VictoryResponse,
                ExpiryResponse = value.ExpiryResponse,
                ResetPolicy = value.ResetPolicy,
                WeeklyResetDay = value.WeeklyResetDay,
                WeeklyResetHourUtc = value.WeeklyResetHourUtc,
            };

        public BlokeRaidConfigurationDraft ToDraft(
            PointAmount specialCost,
            PointAmount victoryReward
        ) =>
            new(
                Revision,
                BossName,
                MaximumHealth,
                MaximumWard,
                CampaignDurationHours,
                Attack.Minimum,
                Attack.Maximum,
                Attack.CooldownSeconds,
                Attack.PerStreamLimit,
                Mend.Minimum,
                Mend.Maximum,
                Mend.CooldownSeconds,
                Mend.PerStreamLimit,
                Special.Minimum,
                Special.Maximum,
                Special.CooldownSeconds,
                Special.PerStreamLimit,
                specialCost,
                CorrectGuessDamage,
                victoryReward,
                PhaseTwoHealthPercent,
                PhaseThreeHealthPercent,
                PhaseOneResponse,
                PhaseTwoResponse,
                PhaseThreeResponse,
                VictoryResponse,
                ExpiryResponse,
                ResetPolicy,
                WeeklyResetDay,
                WeeklyResetHourUtc
            );
    }

    private sealed class ActionRuleEditor
    {
        public int Minimum { get; set; }
        public int Maximum { get; set; }
        public int CooldownSeconds { get; set; }
        public int PerStreamLimit { get; set; }
        public string PointCost { get; set; } = "0";

        public static ActionRuleEditor From(BlokeRaidActionRuleView value) =>
            new()
            {
                Minimum = value.Minimum,
                Maximum = value.Maximum,
                CooldownSeconds = value.CooldownSeconds,
                PerStreamLimit = value.PerStreamLimit,
                PointCost = value.PointCost.ToString(),
            };
    }
}
