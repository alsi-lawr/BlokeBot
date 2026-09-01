namespace BlokeBot.Persistence;

public static partial class MainDatabaseStatements
{
    public static Task<int> EnsureViewerPassportStreamSessionAsync(
        BlokeBotDbContext db,
        int hostId,
        string twitchStreamId,
        DateTime startedAtUtc,
        int continuityGeneration,
        DateTime recordedAtUtc,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.InsertIgnoreAsync(
            db,
            $"""
            INSERT OR IGNORE INTO viewer_passport_stream_sessions
                ("HostId", "TwitchStreamId", "StartedAtUtc", "ContinuityGeneration", "RecordedAtUtc")
            VALUES ({hostId}, {twitchStreamId}, {startedAtUtc}, {continuityGeneration}, {recordedAtUtc});
            """,
            $"""
            INSERT INTO viewer_passport_stream_sessions
                ("HostId", "TwitchStreamId", "StartedAtUtc", "ContinuityGeneration", "RecordedAtUtc")
            VALUES ({hostId}, {twitchStreamId}, {startedAtUtc}, {continuityGeneration}, {recordedAtUtc})
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken
        );

    public static Task<int> TryRecordViewerPassportAttendanceAsync(
        BlokeBotDbContext db,
        int hostId,
        long passportId,
        long streamSessionId,
        int continuityGeneration,
        DateTime firstSeenAtUtc,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.InsertIgnoreAsync(
            db,
            $"""
            INSERT OR IGNORE INTO viewer_passport_stream_attendance
                ("HostId", "PassportId", "StreamSessionId", "ContinuityGeneration", "FirstSeenAtUtc")
            VALUES ({hostId}, {passportId}, {streamSessionId}, {continuityGeneration}, {firstSeenAtUtc});
            """,
            $"""
            INSERT INTO viewer_passport_stream_attendance
                ("HostId", "PassportId", "StreamSessionId", "ContinuityGeneration", "FirstSeenAtUtc")
            VALUES ({hostId}, {passportId}, {streamSessionId}, {continuityGeneration}, {firstSeenAtUtc})
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken
        );

    public static Task<int> TryRecordViewerPassportAmbiguityAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        DateTime detectedAtUtc,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.InsertIgnoreAsync(
            db,
            $"""
            INSERT OR IGNORE INTO viewer_passport_ambiguous_logins
                ("HostId", "Login", "DetectedAtUtc")
            VALUES ({hostId}, {login}, {detectedAtUtc});
            """,
            $"""
            INSERT INTO viewer_passport_ambiguous_logins
                ("HostId", "Login", "DetectedAtUtc")
            VALUES ({hostId}, {login}, {detectedAtUtc})
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken
        );
}
