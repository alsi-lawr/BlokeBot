using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.RaidCollaboration;

public partial class RaidCollaborationPage
{
    private const string _workspaceTabsId = "raid-workspace";
    private const string _hubKey = "hub";
    private const string _settingsKey = "settings";

    private static readonly IReadOnlyList<SegmentedTabItem> _workspaceTabs =
    [
        new(_hubKey, "Hub"),
        new(_settingsKey, "Settings"),
    ];

    [Inject]
    private NavigationManager _navigation { get; set; } = null!;

    private RaidCollaborationDashboard? _dashboard;
    private ConfigurationDraft _draft = new();
    private string _workspaceKey = _hubKey;
    private string _categoryDraft = string.Empty;
    private string _shoutoutTarget = string.Empty;
    private string? _preparedRaidLogin;
    private string? _approvingLogin;
    private string _operationFeedback = string.Empty;
    private bool _operationFailed;
    private bool _loading = true;
    private bool _disabled;
    private bool _loadFailed;
    private bool _saving;
    private PageSaveFeedback? _saveFeedback;
    private IReadOnlyList<AutomaticRaidShoutoutValidationError> _shoutoutErrors = [];

    private static string DescriptionFor(string key) =>
        key == _settingsKey
            ? "Choose how raids are welcomed, shouted out, and shortlisted."
            : "Welcome new communities and choose where to raid next.";

    protected override async Task OnInitializedAsync()
    {
        _workspaceKey = SegmentedTabs.CanonicalKey(_navigation, _workspaceTabs);
        _ = await LoadPageContextAsync();
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [
                    AppEventKind.HostedChannelsChanged,
                    AppEventKind.RaidCollaborationChanged,
                    AppEventKind.TwitchOperationsChanged,
                ],
                InvokeAsync,
                RefreshAsync,
                StateHasChanged
            )
        );
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _loading = true;
        _disabled = false;
        _loadFailed = false;
        _preparedRaidLogin = null;
        _ = await LoadPageContextAsync();
        if (HostId == 0)
        {
            _dashboard = null;
            _loading = false;
            return;
        }
        var outcome = await _service.LoadAsync(HostId, _shoutoutTarget, CancellationToken.None);
        switch (outcome)
        {
            case RaidCollaborationLoadOutcome.Loaded loaded:
                _dashboard = loaded.Dashboard;
                _draft = ConfigurationDraft.From(loaded.Dashboard.Configuration);
                break;
            case RaidCollaborationLoadOutcome.FeatureDisabled:
                _dashboard = null;
                _disabled = true;
                break;
            default:
                _dashboard = null;
                _loadFailed = true;
                break;
        }
        _loading = false;
    }

    private async Task SaveAsync()
    {
        var hostId = HostId;
        _saving = true;
        _saveFeedback = new("Saving raid and collaboration settings…", PageSaveFeedbackKind.Saving);
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                var outcome = await _service.SaveAsync(
                    hostId,
                    _draft.ToConfiguration(),
                    CancellationToken.None
                );
                switch (outcome)
                {
                    case RaidCollaborationSaveOutcome.Saved saved:
                        _draft = ConfigurationDraft.From(saved.Configuration);
                        _shoutoutErrors = [];
                        _saveFeedback = new("Settings saved.", PageSaveFeedbackKind.Success);
                        await LoadDashboardWithoutReplacingFeedbackAsync(hostId);
                        break;
                    case RaidCollaborationSaveOutcome.Invalid invalid:
                        _shoutoutErrors = invalid.ShoutoutErrors;
                        _saveFeedback = new(
                            string.Join(
                                " ",
                                invalid.Errors.Concat(
                                    invalid.ShoutoutErrors.Select(error => error.Message)
                                )
                            ),
                            PageSaveFeedbackKind.Validation
                        );
                        break;
                    case RaidCollaborationSaveOutcome.FeatureDisabled:
                        _saveFeedback = new(
                            "Raid & collaboration was turned off before the save. Nothing changed.",
                            PageSaveFeedbackKind.Failure
                        );
                        break;
                    default:
                        _saveFeedback = new(
                            "The selected channel is no longer available.",
                            PageSaveFeedbackKind.Failure
                        );
                        break;
                }
            }
        );
        _saving = false;
    }

    private async Task LoadDashboardWithoutReplacingFeedbackAsync(int hostId)
    {
        if (
            await _service.LoadAsync(hostId, _shoutoutTarget, CancellationToken.None)
            is RaidCollaborationLoadOutcome.Loaded loaded
        )
        {
            _dashboard = loaded.Dashboard;
        }
    }

    private Task SendShortlistShoutoutAsync(string login) =>
        RunShoutoutAsync(
            login,
            () => _service.SendShortlistShoutoutAsync(HostId, login, CancellationToken.None)
        );

    private Task SendShoutoutAsync() =>
        RunShoutoutAsync(
            _shoutoutTarget,
            () => _service.SendShoutoutAsync(HostId, _shoutoutTarget, CancellationToken.None)
        );

    private async Task RunShoutoutAsync(string login, Func<Task<ShoutoutOperationOutcome>> send)
    {
        _operationFeedback = string.Empty;
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                (_operationFeedback, _operationFailed) = await send() switch
                {
                    ShoutoutOperationOutcome.Sent sent => (
                        $"Shoutout sent to @{sent.TargetLogin}.",
                        false
                    ),
                    ShoutoutOperationOutcome.CooldownActive cooldown => (
                        $"Shoutout cooldown is active until {cooldown.EligibleAtUtc.ToLocalTime():g}.",
                        true
                    ),
                    ShoutoutOperationOutcome.CooldownUnknown => (
                        "Twitch did not confirm the cooldown state.",
                        true
                    ),
                    ShoutoutOperationOutcome.TargetNotFound missing => (
                        $"Twitch user @{missing.TargetLogin} was not found.",
                        true
                    ),
                    ShoutoutOperationOutcome.SelfTarget => (
                        "You cannot shout out the selected channel.",
                        true
                    ),
                    ShoutoutOperationOutcome.TargetOffline => (
                        $"@{login} is no longer live with viewers.",
                        true
                    ),
                    ShoutoutOperationOutcome.NotReady notReady => (notReady.Message, true),
                    _ => ("Twitch rejected the shoutout.", true),
                };
                await LoadDashboardWithoutReplacingFeedbackAsync(HostId);
            }
        );
    }

    private bool IsApproved(string login) =>
        _dashboard is { } dashboard
        && dashboard.Configuration.ApprovedChannels.Any(channel =>
            string.Equals(channel.Login, login, StringComparison.OrdinalIgnoreCase)
        );

    private async Task ApproveAsync(RaidRelationshipHistory entry)
    {
        _approvingLogin = entry.Login;
        _operationFeedback = string.Empty;
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var outcome = await _service.ApproveChannelAsync(
                    HostId,
                    entry.Login,
                    entry.DisplayName,
                    CancellationToken.None
                );
                (_operationFeedback, _operationFailed) = outcome switch
                {
                    ApproveRaidChannelOutcome.Approved approved => (
                        $"@{approved.Channel.Login} is now an approved channel.",
                        false
                    ),
                    ApproveRaidChannelOutcome.AlreadyApproved => (
                        $"@{entry.Login} was already approved.",
                        false
                    ),
                    ApproveRaidChannelOutcome.LimitReached limit => (
                        $"Approve no more than {limit.Limit} channels.",
                        true
                    ),
                    _ => ("Raid & collaboration was turned off. Nothing changed.", true),
                };
                await LoadDashboardWithoutReplacingFeedbackAsync(HostId);
            }
        );
        _approvingLogin = null;
    }

    private void PrepareRaid(string login)
    {
        _preparedRaidLogin = login;
        _operationFeedback = string.Empty;
    }

    private void CancelPreparedRaid() => _preparedRaidLogin = null;

    private async Task ConfirmRaidAsync(string login)
    {
        if (_preparedRaidLogin != login)
        {
            return;
        }
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var outcome = await _service.StartConfirmedRaidAsync(
                    HostId,
                    login,
                    CancellationToken.None
                );
                (_operationFeedback, _operationFailed) = outcome switch
                {
                    ConfirmedRaidStartOutcome.Started => (
                        $"Twitch raid to @{login} is preparing. EventSub will record it once it starts.",
                        false
                    ),
                    ConfirmedRaidStartOutcome.AuthorizationRequired => (
                        "Reconnect the Twitch integration with raid management permission.",
                        true
                    ),
                    ConfirmedRaidStartOutcome.TargetIneligible ineligible => (
                        string.Join(" ", ineligible.Reasons),
                        true
                    ),
                    ConfirmedRaidStartOutcome.FeatureDisabled => (
                        "Raid & collaboration was turned off. No raid was started.",
                        true
                    ),
                    _ => (
                        "Twitch did not start the raid. Refresh live context and try again.",
                        true
                    ),
                };
                _preparedRaidLogin = null;
            }
        );
    }

    private void AddApprovedChannel() => _draft.ApprovedChannels.Add(new());

    private void RemoveApprovedChannel(ApprovedChannelDraft channel) =>
        _ = _draft.ApprovedChannels.Remove(channel);

    private void ClearShoutoutError(AutomaticRaidShoutoutValidationField field) =>
        _shoutoutErrors = _shoutoutErrors.Where(error => error.Field != field).ToArray();

    private void AddCategory()
    {
        var value = _categoryDraft.Trim();
        _categoryDraft = string.Empty;
        if (
            value.Length == 0
            || _draft.Categories.Contains(value, StringComparer.OrdinalIgnoreCase)
        )
        {
            return;
        }

        _draft.Categories.Add(value);
    }

    private void AddCategoryOnEnter(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            AddCategory();
        }
    }

    private void RemoveCategory(int index) => _draft.Categories.RemoveAt(index);

    private static string CooldownText(ShoutoutDashboardState state, string target) =>
        state switch
        {
            {
                GlobalEligibleAtUtc: { } global,
                TargetCooldown: ShoutoutTargetCooldownReadiness.EligibleAt targetCooldown
            } =>
                $"Next global shoutout: {global.ToLocalTime():g}. @{target} is eligible at {targetCooldown.Value.ToLocalTime():g}.",
            { GlobalEligibleAtUtc: { } global } =>
                $"You can send another shoutout after {global.ToLocalTime():g}. No separate time is available yet for the target you typed.",
            { TargetCooldown: ShoutoutTargetCooldownReadiness.EligibleAt targetCooldown } =>
                $"@{target} can be shouted out after {targetCooldown.Value.ToLocalTime():g}. The overall next-send time is not available yet.",
            _ => "No cooldown time is available yet. Try sending when you are ready.",
        };

    private sealed class ConfigurationDraft
    {
        public bool WelcomeEnabled { get; set; }
        public string WelcomeMessage { get; set; } = string.Empty;
        public AutomaticRaidShoutoutConfiguration AutomaticShoutout { get; set; } =
            AutomaticRaidShoutoutConfiguration.Defaults;
        public int DeduplicationWindowMinutes { get; set; }
        public string Language { get; set; } = string.Empty;
        public List<string> Categories { get; } = [];
        public int RelationshipCooldownHours { get; set; }
        public List<ApprovedChannelDraft> ApprovedChannels { get; } = [];

        public static ConfigurationDraft From(RaidCollaborationConfiguration value)
        {
            var draft = new ConfigurationDraft
            {
                WelcomeEnabled = value.WelcomeEnabled,
                WelcomeMessage = value.WelcomeMessage,
                AutomaticShoutout = value.AutomaticShoutout,
                DeduplicationWindowMinutes = value.DeduplicationWindowMinutes,
                Language = value.Language,
                RelationshipCooldownHours = value.RelationshipCooldownHours,
            };
            draft.Categories.AddRange(value.EligibleCategories);
            draft.ApprovedChannels.AddRange(
                value.ApprovedChannels.Select(ApprovedChannelDraft.From)
            );
            return draft;
        }

        public RaidCollaborationConfiguration ToConfiguration() =>
            new(
                WelcomeEnabled,
                WelcomeMessage,
                AutomaticShoutout,
                DeduplicationWindowMinutes,
                Language,
                [.. Categories],
                RelationshipCooldownHours,
                ApprovedChannels.Select(value => value.ToDraft()).ToArray()
            );
    }

    private sealed class ApprovedChannelDraft
    {
        public string Login { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? ApprovedClipId { get; set; }

        public static ApprovedChannelDraft From(ApprovedRaidChannelDraft value) =>
            new()
            {
                Login = value.Login,
                DisplayName = value.DisplayName,
                ApprovedClipId = value.ApprovedClipId,
            };

        public ApprovedRaidChannelDraft ToDraft() => new(Login, DisplayName, ApprovedClipId);
    }
}
