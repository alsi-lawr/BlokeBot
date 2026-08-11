namespace BlokeBot.Persistence.Models;

public enum ViewerPassportVisibility
{
    [PersistedToken("Private")]
    Private,

    [PersistedToken("ChannelMembers")]
    ChannelMembers,

    [PersistedToken("Public")]
    Public,
}

public sealed class ViewerPassport
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public string TwitchUserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProfileLine { get; set; } = string.Empty;
    public ViewerPassportVisibility Visibility { get; set; }
    public bool HideAttendance { get; set; } = true;
    public long? SelectedTitleRewardDefinitionId { get; set; }
    public long? SelectedBadgeRewardDefinitionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ViewerPassportLogin
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long PassportId { get; set; }
    public ViewerPassport? Passport { get; set; }
    public string Login { get; set; } = string.Empty;
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}

public sealed class ViewerPassportAmbiguousLogin
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public string Login { get; set; } = string.Empty;
    public DateTime DetectedAtUtc { get; set; }
}

public sealed class ViewerPassportAttendanceDay
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long PassportId { get; set; }
    public DateOnly DateUtc { get; set; }
    public DateTime FirstSeenAtUtc { get; set; }
}
