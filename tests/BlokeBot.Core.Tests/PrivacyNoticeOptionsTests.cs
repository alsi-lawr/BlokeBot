using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PrivacyNoticeOptionsTests
{
    [Test]
    public void CompletenessGate_AppliesOnlyToOnlineNonLocalEnvironments()
    {
        PrivacyNoticeOptionsValidation.RequiredFor(online: true, "Production").ShouldBeTrue();
        PrivacyNoticeOptionsValidation.RequiredFor(online: true, "Staging").ShouldBeTrue();
        PrivacyNoticeOptionsValidation.RequiredFor(online: true, "Development").ShouldBeFalse();
        PrivacyNoticeOptionsValidation.RequiredFor(online: true, "Simulation").ShouldBeFalse();
        PrivacyNoticeOptionsValidation.RequiredFor(online: false, "Production").ShouldBeFalse();
    }

    [Test]
    public void MalformedContactOrNoticeUrl_NeverSatisfiesCompleteness()
    {
        static PrivacyNoticeOptions Options(string? name, string? contact, string? url) =>
            new()
            {
                ControllerName = name,
                PrivacyContact = contact,
                NoticeUrl = url,
            };

        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", "privacy@blokebot.com", "https://w.example/privacy"))
            .ShouldBeTrue();

        PrivacyNoticeOptionsValidation
            .IsComplete(Options("  ", "privacy@blokebot.com", "https://w.example/privacy"))
            .ShouldBeFalse();
        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", null, "https://w.example/privacy"))
            .ShouldBeFalse();
        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", "no-at-sign", "https://w.example/privacy"))
            .ShouldBeFalse();
        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", "@leading.at", "https://w.example/privacy"))
            .ShouldBeFalse();
        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", "trailing@", "https://w.example/privacy"))
            .ShouldBeFalse();
        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", "two@at@signs", "https://w.example/privacy"))
            .ShouldBeFalse();
        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", "with space@x.example", "https://w.example/privacy"))
            .ShouldBeFalse();
        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", "privacy@blokebot.com", "http://w.example/privacy"))
            .ShouldBeFalse();
        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", "privacy@blokebot.com", "/privacy"))
            .ShouldBeFalse();
        PrivacyNoticeOptionsValidation
            .IsComplete(Options("BlokeBot", "privacy@blokebot.com", null))
            .ShouldBeFalse();
    }
}
