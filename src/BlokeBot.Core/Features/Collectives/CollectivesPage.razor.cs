using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Collectives;

public partial class CollectivesPage
{
    private CollectiveWorkspace? _workspace;
    private bool _featureEnabled;
    private bool _showCreate;
    private bool _showInvite;
    private bool _showWorkflowEditor;
    private bool _failed;
    private string? _feedback;
    private string _newCollectiveName = string.Empty;
    private int _inviteHostId;
    private CollectiveLocalNotification _notification = CollectiveLocalNotification.Moderators;
    private IReadOnlyList<CollectiveKnownHost> _knownHosts = [];
    private IReadOnlyList<CollectiveBountyChoice> _ownedBounties = [];
    private int _tournamentOwnerHostId;
    private Guid _tournamentCompetitionPublicId;
    private string _relayName = string.Empty;
    private int _relayCurrentHostId;
    private int _relayNextHostId;
    private string _goalName = string.Empty;
    private string _goalUnitName = string.Empty;
    private long _goalTarget = 1;
    private DateTime _goalDeadlineUtc = DateTime.UtcNow.AddDays(7);
    private Guid _goalSourceBountyPublicId;

    [SupplyParameterFromQuery(Name = "workflow")]
    public string? RequestedWorkflow { get; set; }

    [SupplyParameterFromQuery(Name = "collective")]
    public Guid? RequestedCollective { get; set; }

    private string _workflow = "tournament";

    private CollectiveDashboard? _selected => _workspace?.SelectedCollective;

    private bool _localSettingsChanged =>
        _selected is not null && _notification != _selected.LocalSettings.Notification;

    private string _workflowTitle =>
        _workflow switch
        {
            "raid" => "Raid relay",
            "goal" => "Cross-channel goal coordination",
            _ => "Shared tournament reference",
        };

    private string _workflowSubtitle =>
        _workflow switch
        {
            "raid" => "Each host controls its own Twitch action",
            "goal" => "Bounded progress without viewer attribution",
            _ => "Bounded state from one host-owned competition",
        };

    private string _privateDetail =>
        _workflow switch
        {
            "raid" =>
                "Welcome rules, cooldowns, approved channels, and provider access remain local.",
            "goal" =>
                "The local goal source, contributor records, rewards, and moderator notes are not shared.",
            _ =>
                "Local reminders, source mapping, private lobby, and contact details are not shared.",
        };

    private string _sharedOutputTitle =>
        _workflow switch
        {
            "raid" => "Aggregate relay state only",
            "goal" => "Collective total and host totals",
            _ => "Read-only tournament status",
        };

    private string _sharedOutputDetail =>
        _workflow switch
        {
            "raid" =>
                "Shared state carries host, bounded aggregate count, hand-off status, and operation identity—never individual viewer data.",
            "goal" =>
                "Public output shows the collective target and bounded host totals. Contributor identities and private goal configuration stay local.",
            _ =>
                "Public results remain owned by the competition host. Other hosts receive the same bounded reference, never private competition data.",
        };

    protected override async Task OnInitializedAsync()
    {
        _workflow = RequestedWorkflow is "raid" or "goal" ? RequestedWorkflow : "tournament";
        await ObserveRouteLoadAsync(LoadAsync);
    }

    private async Task LoadAsync()
    {
        _ = await LoadPageContextAsync();
        if (Host is null)
        {
            return;
        }
        _featureEnabled = await _features.IsEnabledAsync(
            Host.Id,
            HostFeatureFlags.Collectives,
            CancellationToken.None
        );
        if (!_featureEnabled)
        {
            _workspace = null;
            return;
        }
        var outcome = await _service.LoadAsync(
            ReadAuthority(),
            RequestedCollective is { } requested ? new(requested) : null,
            CancellationToken.None
        );
        _workspace = outcome is CollectiveDashboardOutcome.Loaded loaded ? loaded.Workspace : null;
        if (_workspace is { SelectedCollective: { } selected } workspace)
        {
            _notification = selected.LocalSettings.Notification;
            _knownHosts = workspace.KnownHosts;
            _ownedBounties = workspace.OwnedBounties;
            _tournamentOwnerHostId = selected.Tournament is { } tournament
                ? selected
                    .Members.FirstOrDefault(value => value.Login == tournament.OwnerLogin)
                    ?.HostId
                    ?? HostId
                : HostId;
            _tournamentCompetitionPublicId = selected.Tournament?.CompetitionPublicId ?? Guid.Empty;
            _relayName = selected.RaidRelay?.Name ?? string.Empty;
            _relayCurrentHostId = selected.RaidRelay is { } relay
                ? selected
                    .Members.FirstOrDefault(value => value.Login == relay.CurrentHostLogin)
                    ?.HostId
                    ?? HostId
                : HostId;
            _relayNextHostId = selected.RaidRelay?.NextHostLogin is { } nextLogin
                ? selected.Members.FirstOrDefault(value => value.Login == nextLogin)?.HostId ?? 0
                : 0;
            _goalName = selected.Goal?.Name ?? string.Empty;
            _goalUnitName = selected.Goal?.UnitName ?? string.Empty;
            _goalTarget = selected.Goal?.Target ?? 1;
            _goalDeadlineUtc = selected.Goal?.DeadlineUtc ?? DateTime.UtcNow.AddDays(7);
            _goalSourceBountyPublicId = selected.LocalGoalSourcePublicId ?? Guid.Empty;
        }
    }

    private CollectiveAuthority ReadAuthority() =>
        new(
            HostId,
            PageContext.Session.UserId,
            ActorLogin,
            PageContext.Session.CurrentHostRoleIs(AuthRole.Streamer)
                || PageContext.Session.CurrentHostRoleIs(AuthRole.Admin)
                || PageContext.Session.CurrentHostRoleIs(AuthRole.Moderator)
        );

    private CollectiveAuthority MutationAuthority() =>
        new(HostId, PageContext.Session.UserId, ActorLogin, true);

    private void ShowCreate()
    {
        _showCreate = true;
        _showInvite = false;
        _showWorkflowEditor = false;
        _feedback = null;
    }

    private void ShowInvite()
    {
        _showInvite = true;
        _showCreate = false;
        _showWorkflowEditor = false;
        _feedback = null;
    }

    private void ShowWorkflowEditor()
    {
        _showWorkflowEditor = true;
        _showCreate = false;
        _showInvite = false;
        _feedback = null;
    }

    private void CancelFlow()
    {
        _showCreate = false;
        _showInvite = false;
        _showWorkflowEditor = false;
    }

    private async Task CreateAsync() =>
        await RunMutationAsync(authority =>
            _service.CreateAsync(
                new(Guid.NewGuid(), _newCollectiveName, authority),
                CancellationToken.None
            )
        );

    private async Task InviteAsync()
    {
        if (_selected is null || _inviteHostId == 0)
        {
            SetFeedback(new CollectiveMutationOutcome.Invalid("Choose a known host to invite."));
            return;
        }
        await RunMutationAsync(authority =>
            _service.InviteAsync(
                new(Guid.NewGuid(), _selected.Id, _inviteHostId, authority),
                CancellationToken.None
            )
        );
    }

    private Task AcceptAsync() =>
        _selected is null
            ? Task.CompletedTask
            : RunMutationAsync(authority =>
                _service.AcceptInvitationAsync(
                    new(Guid.NewGuid(), _selected.Id, authority),
                    CancellationToken.None
                )
            );

    private Task DeclineAsync() =>
        _selected is null
            ? Task.CompletedTask
            : RunMutationAsync(authority =>
                _service.DeclineInvitationAsync(
                    new(Guid.NewGuid(), _selected.Id, authority),
                    CancellationToken.None
                )
            );

    private Task LeaveAsync() =>
        _selected is null
            ? Task.CompletedTask
            : RunMutationAsync(authority =>
                _service.LeaveAsync(
                    new(Guid.NewGuid(), _selected.Id, authority),
                    CancellationToken.None
                )
            );

    private Task WithdrawAsync(int hostId) =>
        _selected is null
            ? Task.CompletedTask
            : RunMutationAsync(authority =>
                _service.WithdrawInvitationAsync(
                    new(Guid.NewGuid(), _selected.Id, hostId, authority),
                    CancellationToken.None
                )
            );

    private Task RevokeAsync(int hostId) =>
        _selected is null
            ? Task.CompletedTask
            : RunMutationAsync(authority =>
                _service.RevokeAsync(
                    new(Guid.NewGuid(), _selected.Id, hostId, authority),
                    CancellationToken.None
                )
            );

    private Task TransferCoordinationAsync(int hostId) =>
        _selected is null
            ? Task.CompletedTask
            : RunMutationAsync(authority =>
                _service.TransferCoordinationAsync(
                    new(Guid.NewGuid(), _selected.Id, hostId, authority),
                    CancellationToken.None
                )
            );

    private Task SaveWorkflowAsync() =>
        _selected is null
            ? Task.CompletedTask
            : _workflow switch
            {
                "tournament" => RunMutationAsync(authority =>
                    _service.SetTournamentReferenceAsync(
                        new(
                            Guid.NewGuid(),
                            _selected.Id,
                            _tournamentOwnerHostId,
                            _tournamentCompetitionPublicId,
                            authority
                        ),
                        CancellationToken.None
                    )
                ),
                "raid" => RunMutationAsync(authority =>
                    _service.ConfigureRaidRelayAsync(
                        new(
                            Guid.NewGuid(),
                            _selected.Id,
                            _relayName,
                            _relayCurrentHostId,
                            _relayNextHostId == 0 ? null : _relayNextHostId,
                            authority
                        ),
                        CancellationToken.None
                    )
                ),
                _ => RunMutationAsync(authority =>
                    _service.ConfigureGoalAsync(
                        new(
                            Guid.NewGuid(),
                            _selected.Id,
                            _goalName,
                            _goalUnitName,
                            _goalTarget,
                            DateTime.SpecifyKind(_goalDeadlineUtc, DateTimeKind.Utc),
                            [],
                            authority
                        ),
                        CancellationToken.None
                    )
                ),
            };

    private Task SetGoalSourceAsync() =>
        _selected is null || _goalSourceBountyPublicId == Guid.Empty
            ? Task.CompletedTask
            : RunMutationAsync(authority =>
                _service.SetGoalSourceAsync(
                    new(Guid.NewGuid(), _selected.Id, _goalSourceBountyPublicId, authority),
                    CancellationToken.None
                )
            );

    private Task ConfirmHandoffAsync() =>
        _selected?.RaidRelay is not { } relay
            ? Task.CompletedTask
            : RunMutationAsync(authority =>
                _service.ConfirmRaidHandoffAsync(
                    new(Guid.NewGuid(), _selected.Id, relay.Revision, authority),
                    CancellationToken.None
                )
            );

    private Task SaveLocalSettingsAsync() =>
        _selected is null
            ? Task.CompletedTask
            : RunMutationAsync(authority =>
                _service.SaveLocalSettingsAsync(
                    new(
                        Guid.NewGuid(),
                        _selected.Id,
                        _selected.LocalSettings.Revision,
                        _notification,
                        authority
                    ),
                    CancellationToken.None
                )
            );

    private Task RunMutationAsync(
        Func<CollectiveAuthority, Task<CollectiveMutationOutcome>> mutation
    ) =>
        ObserveUiOperationAsync(
            nameof(RunMutationAsync),
            async () =>
                await RunSelectedHostMutationAsync(
                    HostId,
                    async () =>
                    {
                        var outcome = await mutation(MutationAuthority());
                        SetFeedback(outcome);
                        if (outcome is CollectiveMutationOutcome.Succeeded succeeded)
                        {
                            RequestedCollective = succeeded.CollectiveId.Value;
                            _showCreate = false;
                            _showInvite = false;
                            _showWorkflowEditor = false;
                            await LoadAsync();
                        }
                    }
                )
        );

    private async Task SelectCollectiveAsync(ChangeEventArgs args)
    {
        RequestedCollective = Guid.TryParse(args.Value?.ToString(), out var selected)
            ? selected
            : null;
        await LoadAsync();
    }

    private RenderFragment WorkflowTab(string key, string label) =>
        builder =>
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "type", "button");
            builder.AddAttribute(2, "role", "tab");
            builder.AddAttribute(
                3,
                "aria-selected",
                (_workflow == key).ToString().ToLowerInvariant()
            );
            builder.AddAttribute(4, "aria-controls", "collective-workflow-panel");
            builder.AddAttribute(
                5,
                "class",
                _workflow == key ? "collective-tab collective-tab--active" : "collective-tab"
            );
            builder.AddAttribute(
                6,
                "onclick",
                EventCallback.Factory.Create(this, () => _workflow = key)
            );
            builder.AddContent(7, label);
            builder.CloseElement();
        };

    private void SetFeedback(CollectiveMutationOutcome outcome) =>
        (_feedback, _failed) = outcome switch
        {
            CollectiveMutationOutcome.Succeeded { WasIdempotent: true } => (
                "That operation was already recorded once.",
                false
            ),
            CollectiveMutationOutcome.Succeeded => ("Collective saved.", false),
            CollectiveMutationOutcome.FeatureDisabled => (
                "Collectives is off for a host affected by that operation.",
                true
            ),
            CollectiveMutationOutcome.AuthorityRequired => (
                "The selected host authority changed. Choose a channel and try again.",
                true
            ),
            CollectiveMutationOutcome.LastCoordinatorRequired => (
                "Transfer coordination before removing the last active coordinator.",
                true
            ),
            CollectiveMutationOutcome.Invalid invalid => (invalid.Message, true),
            CollectiveMutationOutcome.Conflict conflict => (conflict.Message, true),
            CollectiveMutationOutcome.NotFound => ("The collective or host was not found.", true),
            CollectiveMutationOutcome.ProviderRejected => (
                "Twitch did not accept that host's raid handoff.",
                true
            ),
            _ => ("The collective could not be changed.", true),
        };

    private string PublicUrl(CollectiveId collectiveId) =>
        $"/collectives/{HostLogin}/{collectiveId.Value:D}";

    private static int WorkflowCount(CollectiveDashboard dashboard) =>
        (dashboard.Tournament is null ? 0 : 1)
        + (dashboard.RaidRelay is null ? 0 : 1)
        + (dashboard.Goal is null ? 0 : 1);

    private static string MembershipLabel(CollectiveMemberProjection member) =>
        member.Status switch
        {
            CollectiveMembershipStatus.Active
                when member.Role == CollectiveMembershipRole.Coordinator =>
                "✓ Host accepted · coordinator",
            CollectiveMembershipStatus.Active => "✓ Host accepted",
            CollectiveMembershipStatus.Pending => "◷ Invitation awaiting host",
            CollectiveMembershipStatus.Declined => "Declined by host",
            CollectiveMembershipStatus.Left => "Host left",
            _ => "Participation revoked",
        };

    private static string AuditLabel(CollectiveAuditAction action) =>
        action.ToString().Replace("Changed", " updated", StringComparison.Ordinal);

    private static string Initials(string displayName) =>
        string.Concat(
                displayName
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Take(2)
                    .Select(value => char.ToUpperInvariant(value[0]))
            )
            .PadRight(2, '•');

    private static string ShortId(Guid value) => value.ToString("N")[..8].ToUpperInvariant();

    private static string Relative(DateTime occurredAtUtc) =>
        occurredAtUtc.ToString("dd MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static int Percent(long current, long target) =>
        target <= 0 ? 0 : (int)Math.Clamp(current * 100 / target, 0, 100);

    private static string ProgressStyle(long current, long target) =>
        $"width: {Percent(current, target)}%";

    private static string Plural(string unit, long count) => count == 1 ? unit : $"{unit}s";
}
