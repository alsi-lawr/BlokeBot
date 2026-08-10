using System.Diagnostics;

namespace BlokeBot.Persistence.Models;

public enum PointLedgerKind
{
    Add,
    Remove,
    DeleteBalance,
    TransferOut,
    TransferIn,
    GambleWin,
    GambleLoss,
    GiveawayWin,
    GuessWin,
    RequestReservation,
    RequestRefund,
    MomentReward,
    BountyPledgeReservation,
    BountyPledgeRefund,
    BountyPledgeConsumption,
    BountyCompletionReward,
    CommunityProgressionReward,
}

internal static class PointLedgerKindPersistence
{
    private const string _addToken = "Add";
    private const string _removeToken = "Remove";
    private const string _deleteBalanceToken = "DeleteBalance";
    private const string _transferOutToken = "TransferOut";
    private const string _transferInToken = "TransferIn";
    private const string _gambleWinToken = "GambleWin";
    private const string _gambleLossToken = "GambleLoss";
    private const string _giveawayWinToken = "GiveawayWin";
    private const string _guessWinToken = "GuessWin";
    private const string _requestReservationToken = "RequestReservation";
    private const string _requestRefundToken = "RequestRefund";
    private const string _momentRewardToken = "MomentReward";
    private const string _bountyPledgeReservationToken = "BountyPledgeReservation";
    private const string _bountyPledgeRefundToken = "BountyPledgeRefund";
    private const string _bountyPledgeConsumptionToken = "BountyPledgeConsumption";
    private const string _bountyCompletionRewardToken = "BountyCompletionReward";
    private const string _communityProgressionRewardToken = "CommunityProgressionReward";

    public static IReadOnlyList<string> Tokens { get; } =
    [
        _addToken,
        _removeToken,
        _deleteBalanceToken,
        _transferOutToken,
        _transferInToken,
        _gambleWinToken,
        _gambleLossToken,
        _giveawayWinToken,
        _guessWinToken,
        _requestReservationToken,
        _requestRefundToken,
        _momentRewardToken,
        _bountyPledgeReservationToken,
        _bountyPledgeRefundToken,
        _bountyPledgeConsumptionToken,
        _bountyCompletionRewardToken,
        _communityProgressionRewardToken,
    ];

    public static string ToToken(PointLedgerKind kind) =>
        kind switch
        {
            PointLedgerKind.Add => _addToken,
            PointLedgerKind.Remove => _removeToken,
            PointLedgerKind.DeleteBalance => _deleteBalanceToken,
            PointLedgerKind.TransferOut => _transferOutToken,
            PointLedgerKind.TransferIn => _transferInToken,
            PointLedgerKind.GambleWin => _gambleWinToken,
            PointLedgerKind.GambleLoss => _gambleLossToken,
            PointLedgerKind.GiveawayWin => _giveawayWinToken,
            PointLedgerKind.GuessWin => _guessWinToken,
            PointLedgerKind.RequestReservation => _requestReservationToken,
            PointLedgerKind.RequestRefund => _requestRefundToken,
            PointLedgerKind.MomentReward => _momentRewardToken,
            PointLedgerKind.BountyPledgeReservation => _bountyPledgeReservationToken,
            PointLedgerKind.BountyPledgeRefund => _bountyPledgeRefundToken,
            PointLedgerKind.BountyPledgeConsumption => _bountyPledgeConsumptionToken,
            PointLedgerKind.BountyCompletionReward => _bountyCompletionRewardToken,
            PointLedgerKind.CommunityProgressionReward => _communityProgressionRewardToken,
            _ => throw new UnreachableException("Unknown point ledger kind."),
        };

    public static PointLedgerKind FromToken(string token) =>
        token switch
        {
            _addToken => PointLedgerKind.Add,
            _removeToken => PointLedgerKind.Remove,
            _deleteBalanceToken => PointLedgerKind.DeleteBalance,
            _transferOutToken => PointLedgerKind.TransferOut,
            _transferInToken => PointLedgerKind.TransferIn,
            _gambleWinToken => PointLedgerKind.GambleWin,
            _gambleLossToken => PointLedgerKind.GambleLoss,
            _giveawayWinToken => PointLedgerKind.GiveawayWin,
            _guessWinToken => PointLedgerKind.GuessWin,
            _requestReservationToken => PointLedgerKind.RequestReservation,
            _requestRefundToken => PointLedgerKind.RequestRefund,
            _momentRewardToken => PointLedgerKind.MomentReward,
            _bountyPledgeReservationToken => PointLedgerKind.BountyPledgeReservation,
            _bountyPledgeRefundToken => PointLedgerKind.BountyPledgeRefund,
            _bountyPledgeConsumptionToken => PointLedgerKind.BountyPledgeConsumption,
            _bountyCompletionRewardToken => PointLedgerKind.BountyCompletionReward,
            _communityProgressionRewardToken => PointLedgerKind.CommunityProgressionReward,
            _ => throw new PersistenceDataIntegrityException(typeof(PointLedgerKind)),
        };
}
