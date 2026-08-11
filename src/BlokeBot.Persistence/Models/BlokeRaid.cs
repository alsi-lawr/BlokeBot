namespace BlokeBot.Persistence.Models;

public enum BlokeRaidCampaignStatus
{
    [PersistedToken("Active")]
    Active,

    [PersistedToken("Victory")]
    Victory,

    [PersistedToken("Expired")]
    Expired,

    [PersistedToken("Ended")]
    Ended,
}

public enum BlokeRaidResetPolicy
{
    [PersistedToken("Manual")]
    Manual,

    [PersistedToken("Weekly")]
    Weekly,
}

public enum BlokeRaidActionKind
{
    [PersistedToken("Attack")]
    Attack,

    [PersistedToken("Mend")]
    Mend,

    [PersistedToken("Special")]
    Special,

    [PersistedToken("CorrectGuess")]
    CorrectGuess,
}

public enum BlokeRaidActionSource
{
    [PersistedToken("Chat")]
    Chat,

    [PersistedToken("Guessing")]
    Guessing,
}

public enum BlokeRaidEventKind
{
    [PersistedToken("CampaignStarted")]
    CampaignStarted,

    [PersistedToken("ActionResolved")]
    ActionResolved,

    [PersistedToken("PhaseChanged")]
    PhaseChanged,

    [PersistedToken("CampaignVictorious")]
    CampaignVictorious,

    [PersistedToken("CampaignExpired")]
    CampaignExpired,

    [PersistedToken("CampaignEnded")]
    CampaignEnded,

    [PersistedToken("CampaignReset")]
    CampaignReset,
}

public sealed class BlokeRaidConfiguration
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public int Revision { get; set; }
    public string BossName { get; set; } = "The Null Wyrm";
    public int MaximumHealth { get; set; } = 25_000;
    public int MaximumWard { get; set; } = 1_000;
    public int CampaignDurationHours { get; set; } = 168;
    public int AttackMinimum { get; set; } = 2;
    public int AttackMaximum { get; set; } = 6;
    public int AttackCooldownSeconds { get; set; } = 20;
    public int AttackPerStreamLimit { get; set; } = 40;
    public int MendMinimum { get; set; } = 3;
    public int MendMaximum { get; set; } = 7;
    public int MendCooldownSeconds { get; set; } = 30;
    public int MendPerStreamLimit { get; set; } = 20;
    public int SpecialMinimum { get; set; } = 8;
    public int SpecialMaximum { get; set; } = 14;
    public int SpecialCooldownSeconds { get; set; } = 90;
    public int SpecialPerStreamLimit { get; set; } = 5;
    public string SpecialPointCost { get; set; } = "75";
    public int CorrectGuessDamage { get; set; } = 4;
    public string VictoryPointReward { get; set; } = "250";
    public int PhaseTwoHealthPercent { get; set; } = 65;
    public int PhaseThreeHealthPercent { get; set; } = 30;
    public string PhaseOneResponse { get; set; } =
        "The Null Wyrm descends. Rally the channel and break its guard.";
    public string PhaseTwoResponse { get; set; } =
        "Its armour fractures. The raid drives into the exposed scales.";
    public string PhaseThreeResponse { get; set; } =
        "The Wyrm is cornered. One final push will finish the raid.";
    public string VictoryResponse { get; set; } =
        "The Null Wyrm falls. Every contributor earns the victory reward.";
    public string ExpiryResponse { get; set; } =
        "The Null Wyrm escaped. The campaign remains in the raid history.";
    public BlokeRaidResetPolicy ResetPolicy { get; set; } = BlokeRaidResetPolicy.Manual;
    public int WeeklyResetDay { get; set; } = (int)DayOfWeek.Monday;
    public int WeeklyResetHourUtc { get; set; } = 9;
    public DateTime? NextWeeklyResetAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class BlokeRaidCampaign
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public Guid PublicId { get; set; }
    public string StartOperationKey { get; set; } = string.Empty;
    public BlokeRaidCampaignStatus Status { get; set; } = BlokeRaidCampaignStatus.Active;
    public string BossName { get; set; } = string.Empty;
    public int MaximumHealth { get; set; }
    public int CurrentHealth { get; set; }
    public int MaximumWard { get; set; }
    public int CurrentWard { get; set; }
    public int CurrentPhase { get; set; } = 1;
    public string VictoryPointReward { get; set; } = "0";
    public BlokeRaidResetPolicy ResetPolicy { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? VictoryRewardedAtUtc { get; set; }
    public int Revision { get; set; }
    public List<BlokeRaidAction> Actions { get; set; } = [];
    public List<BlokeRaidContribution> Contributions { get; set; } = [];
    public List<BlokeRaidDomainEvent> Events { get; set; } = [];
}

public sealed class BlokeRaidAction
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long CampaignId { get; set; }
    public BlokeRaidCampaign? Campaign { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public BlokeRaidActionKind Kind { get; set; }
    public BlokeRaidActionSource Source { get; set; }
    public string? ViewerTwitchUserId { get; set; }
    public string? ViewerLogin { get; set; }
    public string? ViewerDisplayName { get; set; }
    public string StreamKey { get; set; } = string.Empty;
    public int Outcome { get; set; }
    public string PointCost { get; set; } = "0";
    public int BossHealthBefore { get; set; }
    public int BossHealthAfter { get; set; }
    public int WardBefore { get; set; }
    public int WardAfter { get; set; }
    public int PhaseAfter { get; set; }
    public int? GuessRoundId { get; set; }
    public string Response { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class BlokeRaidContribution
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long CampaignId { get; set; }
    public BlokeRaidCampaign? Campaign { get; set; }
    public string ViewerTwitchUserId { get; set; } = string.Empty;
    public string ViewerLogin { get; set; } = string.Empty;
    public string ViewerDisplayName { get; set; } = string.Empty;
    public int Damage { get; set; }
    public int WardRestored { get; set; }
    public int ActionCount { get; set; }
    public int SpecialCount { get; set; }
    public int CorrectGuessCount { get; set; }
    public DateTime LastContributedAtUtc { get; set; }
}

public sealed class BlokeRaidDomainEvent
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long CampaignId { get; set; }
    public BlokeRaidCampaign? Campaign { get; set; }
    public BlokeRaidEventKind Kind { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string PublicPayload { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
