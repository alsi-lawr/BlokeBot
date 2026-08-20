using BlokeBot.Announcements;
using BlokeBot.Core.Features.CustomCommands;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class WeeklyAnnouncementScheduleProjectionTests
{
    [Test]
    public void UtcRecurrence_ProjectingAcrossUtcDayBoundary_UsesDestinationLocalDay()
    {
        var schedule = new WeeklyAnnouncementSchedule(DayOfWeek.Sunday, new TimeOnly(23, 30));
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

        var local = WeeklyAnnouncementScheduleProjection.ToLocal(
            schedule,
            timeZone,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
        );

        local.Day.ShouldBe(DayOfWeek.Monday);
        local.Time.ShouldBe(new TimeOnly(11, 30));
    }

    [Test]
    public void FixedUtcRecurrence_ProjectingAcrossDst_ChangesDisplayedLocalTimeOnly()
    {
        var schedule = new WeeklyAnnouncementSchedule(DayOfWeek.Sunday, new TimeOnly(1, 30));
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        var summer = WeeklyAnnouncementScheduleProjection.ToLocal(
            schedule,
            timeZone,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
        );
        var winter = WeeklyAnnouncementScheduleProjection.ToLocal(
            schedule,
            timeZone,
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)
        );

        summer.ShouldBe(new WeeklyAnnouncementSchedule(DayOfWeek.Sunday, new TimeOnly(2, 30)));
        winter.ShouldBe(new WeeklyAnnouncementSchedule(DayOfWeek.Sunday, new TimeOnly(1, 30)));
    }

    [Test]
    public void EditorTimeZoneChange_ReprojectsDisplayAndPreservesUtcRecurrence()
    {
        var reference = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var editor = WeeklyAnnouncementScheduleEditorProjection.FromUtc(
            DayOfWeek.Sunday,
            new TimeOnly(1, 30),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/London"),
            reference
        );
        var configuration = new CustomCommandConfiguration
        {
            TimeZoneId = "Europe/London",
            ProjectionReferenceUtc = reference,
            Announcements = [new() { Schedule = editor }],
        };

        WeeklyAnnouncementScheduleEditorProjection.ChangeTimeZone(
            configuration,
            "America/Los_Angeles"
        );

        configuration.TimeZoneId.ShouldBe("America/Los_Angeles");
        editor.Day.ShouldBe(DayOfWeek.Saturday);
        editor.Time.ShouldBe(new TimeOnly(18, 30));
        WeeklyAnnouncementScheduleEditorProjection
            .ToUtc(editor, TimeZoneInfo.FindSystemTimeZoneById(configuration.TimeZoneId), reference)
            .ShouldBe(new WeeklyAnnouncementSchedule(DayOfWeek.Sunday, new TimeOnly(1, 30)));
    }
}
