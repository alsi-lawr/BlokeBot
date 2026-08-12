using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
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
    private string? _preparedRaidLogin;
    private string _operationFeedback = string.Empty;
    private bool _operationFailed;
    private bool _loading = true;
    private bool _disabled;
    private bool _loadFailed;
    private bool _saving;
    private PageSaveFeedback? _saveFeedback;

    private static string DescriptionFor(string key) =>
        key == _settingsKey
            ? "Choose how raids are welcomed and shortlisted."
            : "Welcome new communities and choose where to raid next.";

    protected override async Task OnInitializedAsync()
    {
        _workspaceKey = SegmentedTabs.CanonicalKey(_navigation, _workspaceTabs);
        _ = await LoadPageContextAsync();
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.RaidCollaborationChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
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
        var outcome = await _service.LoadAsync(HostId, CancellationToken.None);
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
                        _saveFeedback = new("Settings saved.", PageSaveFeedbackKind.Success);
                        await LoadDashboardWithoutReplacingFeedbackAsync(hostId);
                        break;
                    case RaidCollaborationSaveOutcome.Invalid invalid:
                        _saveFeedback = new(
                            string.Join(" ", invalid.Errors),
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
            await _service.LoadAsync(hostId, CancellationToken.None)
            is RaidCollaborationLoadOutcome.Loaded loaded
        )
        {
            _dashboard = loaded.Dashboard;
        }
    }

    private async Task SendShoutoutAsync(string login)
    {
        _operationFeedback = string.Empty;
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var outcome = await _service.SendShoutoutAsync(
                    HostId,
                    login,
                    CancellationToken.None
                );
                (_operationFeedback, _operationFailed) = outcome switch
                {
                    ShoutoutOperationOutcome.Sent => ($"Shoutout sent to @{login}.", false),
                    ShoutoutOperationOutcome.CooldownActive cooldown => (
                        $"Shoutout cooldown is active until {cooldown.EligibleAtUtc.ToLocalTime():g}.",
                        true
                    ),
                    ShoutoutOperationOutcome.CooldownUnknown => (
                        "Twitch did not confirm the cooldown state.",
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

    private static string ShoutoutReadiness(ShoutoutDashboardState state) =>
        state.GlobalEligibleAtUtc is { } eligibleAt
            ? $"Next global shoutout: {eligibleAt.ToLocalTime():g}"
            : "No global cooldown is recorded. Twitch checks again when you send.";

    private sealed class ConfigurationDraft
    {
        public bool WelcomeEnabled { get; set; }
        public string WelcomeMessage { get; set; } = string.Empty;
        public bool NativeShoutoutEnabled { get; set; }
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
                NativeShoutoutEnabled = value.NativeShoutoutEnabled,
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
                NativeShoutoutEnabled,
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
