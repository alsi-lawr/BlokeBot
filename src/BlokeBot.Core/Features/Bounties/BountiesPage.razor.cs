using System.Diagnostics;
using System.Globalization;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Bounties;

public partial class BountiesPage
{
    private IReadOnlyList<BountyModeratorView> _items = [];
    private readonly Dictionary<Guid, string> _reasons = [];
    private readonly Dictionary<Guid, string> _extensions = [];
    private BountyDraft _draft = BountyDraft.New();
    private bool _bountiesConfigured;
    private bool _pointsEnabled;
    private bool _featureEnabled;
    private bool _operationFailed;
    private string _feedback = string.Empty;

    private string _publicBoardUrl => $"/bounties/{Uri.EscapeDataString(HostLogin)}";

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (HostId == 0)
        {
            return;
        }

        var features = await _features.Load(HostId).ExecuteAsync(CancellationToken.None);
        var configured = features.Match(
            option => option.Match(value => value, () => HostFeatureFlags.None),
            _ => throw new UnreachableException()
        );
        _bountiesConfigured = configured.Contains(HostFeatureFlags.Bounties);
        _pointsEnabled = configured.Contains(HostFeatureFlags.Points);
        _featureEnabled = _bountiesConfigured && _pointsEnabled;
        _items = _featureEnabled
            ? await _bounties.GetModeratorBoardAsync(HostId, CancellationToken.None)
            : [];
    }

    private async Task CreateAsync()
    {
        if (!TryPointAmount(_draft.FundingTarget, out var target) || target.IsZero)
        {
            Fail("Funding target must be a positive whole number.");
            return;
        }
        if (!TryPointAmount(_draft.CompletionReward, out var reward))
        {
            Fail("Completion bonus must be a non-negative whole number.");
            return;
        }
        if (!TryUtc(_draft.ExpiresAtUtc, out var expiry))
        {
            Fail("Expiry must be a valid UTC date and time.");
            return;
        }

        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await _bounties.CreateAsync(
                    HostId,
                    new CreateBountyCommand(
                        Guid.NewGuid(),
                        _draft.Title,
                        _draft.Description,
                        target,
                        expiry,
                        reward,
                        _draft.Visibility,
                        _draft.FailurePolicy,
                        _draft.RewardDistribution,
                        Actor(),
                        _draft.Reason
                    ),
                    CancellationToken.None
                );
                _feedback = result.Match(
                    static succeeded => $"Created proposed bounty {succeeded.Value.Title}.",
                    static rejected => rejected.Reason.Message
                );
                _operationFailed = result is BountyResult<BountyView>.Rejected;
                if (!_operationFailed)
                {
                    _draft = BountyDraft.New();
                    await LoadAsync();
                }
            }
        );
    }

    private async Task TransitionAsync(BountyView bounty, BountyTransitionAction action) =>
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await _bounties.TransitionAsync(
                    HostId,
                    new TransitionBountyCommand(
                        Guid.NewGuid(),
                        bounty.PublicId,
                        bounty.Revision,
                        action,
                        Actor(),
                        ReasonFor(bounty.PublicId)
                    ),
                    CancellationToken.None
                );
                _feedback = result.Match(
                    succeeded => $"{succeeded.Value.Title} is now {succeeded.Value.Status}.",
                    static rejected => rejected.Reason.Message
                );
                _operationFailed = result is BountyResult<BountyView>.Rejected;
                await LoadAsync();
            }
        );

    private async Task ExtendAsync(BountyView bounty)
    {
        if (!TryUtc(ExtensionFor(bounty.PublicId), out var expiry))
        {
            Fail("Extension must be a valid UTC date and time.");
            return;
        }

        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var result = await _bounties.ExtendAsync(
                    HostId,
                    new ExtendBountyCommand(
                        Guid.NewGuid(),
                        bounty.PublicId,
                        bounty.Revision,
                        expiry,
                        Actor(),
                        ReasonFor(bounty.PublicId)
                    ),
                    CancellationToken.None
                );
                _feedback = result.Match(
                    static succeeded => $"Expiry extended to {succeeded.Value.ExpiresAtUtc:u}.",
                    static rejected => rejected.Reason.Message
                );
                _operationFailed = result is BountyResult<BountyView>.Rejected;
                await LoadAsync();
            }
        );
    }

    private BountyActor Actor() => new(PageContext.Session.UserId, PageContext.Session.Login);

    private string ReasonFor(Guid id) => _reasons.GetValueOrDefault(id, string.Empty);

    private void SetReason(Guid id, string value) => _reasons[id] = value;

    private string ExtensionFor(Guid id) =>
        _extensions.GetValueOrDefault(
            id,
            DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        );

    private void SetExtension(Guid id, string value) => _extensions[id] = value;

    private void Fail(string message)
    {
        _feedback = message;
        _operationFailed = true;
    }

    private static bool TryPointAmount(string value, out PointAmount amount)
    {
        var parsed = PointAmount
            .ParseNonNegativeAbsolute(value)
            .Match<PointAmount?>(static success => success, static _ => null);
        amount = parsed ?? PointAmount.Zero;
        return parsed.HasValue;
    }

    private static bool TryUtc(string value, out DateTime result)
    {
        if (
            DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
        )
        {
            result = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        result = default;
        return false;
    }

    private static IReadOnlyList<BountyTransitionAction> AvailableActions(BountyStatus status) =>
        status switch
        {
            BountyStatus.Proposed =>
            [
                BountyTransitionAction.OpenFunding,
                BountyTransitionAction.Reject,
            ],
            BountyStatus.Funding =>
            [
                BountyTransitionAction.Accept,
                BountyTransitionAction.Reject,
                BountyTransitionAction.Cancel,
            ],
            BountyStatus.Accepted =>
            [
                BountyTransitionAction.Complete,
                BountyTransitionAction.Fail,
                BountyTransitionAction.Cancel,
            ],
            _ => [],
        };

    private static bool CanExtend(BountyStatus status) =>
        status is BountyStatus.Funding or BountyStatus.Accepted;

    private static string ActionLabel(BountyTransitionAction action) =>
        action switch
        {
            BountyTransitionAction.OpenFunding => "Open funding",
            BountyTransitionAction.Accept => "Accept",
            BountyTransitionAction.Complete => "Complete",
            BountyTransitionAction.Fail => "Fail",
            BountyTransitionAction.Cancel => "Cancel",
            BountyTransitionAction.Reject => "Reject",
            BountyTransitionAction.Expire => "Expire",
            _ => throw new UnreachableException(),
        };

    private static string ShortId(Guid id) => id.ToString("N")[..8];

    private sealed class BountyDraft
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FundingTarget { get; set; } = "100";
        public string CompletionReward { get; set; } = "0";
        public string ExpiresAtUtc { get; set; } = string.Empty;
        public BountyVisibility Visibility { get; set; } = BountyVisibility.Public;
        public BountyFailurePledgePolicy FailurePolicy { get; set; } =
            BountyFailurePledgePolicy.Refund;
        public BountyRewardDistribution RewardDistribution { get; set; } =
            BountyRewardDistribution.Proportional;
        public string Reason { get; set; } = string.Empty;

        public static BountyDraft New() =>
            new()
            {
                ExpiresAtUtc = DateTime
                    .UtcNow.AddDays(7)
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            };
    }
}
