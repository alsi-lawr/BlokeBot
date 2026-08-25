using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class RuntimeSessionBoundaryTests : RuntimeSessionResilienceTestBase
{
    [Test]
    public void StructuredHealthReport_Logging_ContainsSafeFieldsWithoutExceptionMessage()
    {
        const string Secret = "oauth:do-not-log";
        var logger = new RecordingLogger<RuntimeSessionHealthLogger>();
        var health = new RuntimeSessionHealthLogger(logger);

        health.Report(
            new RuntimeSessionHealthReport.Unhealthy
            {
                Runtime = ChatRuntime.Irc,
                Classification = RuntimeSessionFailureClassification.Unexpected,
                Attempt = 2,
                Exception = new ApplicationException(Secret),
            }
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(Secret);
        entry.Properties["Runtime"].ShouldBe(ChatRuntime.Irc);
        entry.Properties["Classification"].ShouldBe(RuntimeSessionFailureClassification.Unexpected);
        entry.Properties["Attempt"].ShouldBe(2);
        entry.Properties["FailureType"].ShouldBe(typeof(ApplicationException).FullName);
    }

    [Test]
    public void StructuredReconnectReport_Logging_ContainsSafeFieldsWithoutExceptionMessage()
    {
        const string Secret = "oauth:do-not-log";
        var logger = new RecordingLogger<RuntimeSessionHealthLogger>();
        var health = new RuntimeSessionHealthLogger(logger);

        health.Report(
            new RuntimeSessionHealthReport.ReconnectScheduled
            {
                Runtime = ChatRuntime.Irc,
                Classification = RuntimeSessionFailureClassification.Transient,
                Attempt = 3,
                Exception = new IOException(Secret),
            }
        );

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(Secret);
        entry.Properties["Runtime"].ShouldBe(ChatRuntime.Irc);
        entry.Properties["Classification"].ShouldBe(RuntimeSessionFailureClassification.Transient);
        entry.Properties["Attempt"].ShouldBe(3);
        entry.Properties["FailureType"].ShouldBe(typeof(IOException).FullName);
    }
}
