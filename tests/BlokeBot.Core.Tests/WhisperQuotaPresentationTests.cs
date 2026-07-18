using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class WhisperQuotaPresentationTests
{
    [Test]
    public void HealthyQuota_Presenting_UsesCompactHealthyStatus()
    {
        var presentation = WhisperQuotaPresentation.From(
            new WhisperQuotaStatus(0, WhisperQuotaService.UniqueRecipientLimit, false)
        );

        presentation.Text.ShouldBe("0/40");
        presentation.State.ShouldBe(WhisperQuotaPresentationState.Healthy);
    }

    [Test]
    public void CautionQuota_Presenting_UsesCompactCautionStatus()
    {
        var presentation = WhisperQuotaPresentation.From(
            new WhisperQuotaStatus(30, WhisperQuotaService.UniqueRecipientLimit, false)
        );

        presentation.Text.ShouldBe("30/40");
        presentation.State.ShouldBe(WhisperQuotaPresentationState.Caution);
    }

    [Test]
    public void QuotaAtLimit_Presenting_UsesCompactLimitStatus()
    {
        var presentation = WhisperQuotaPresentation.From(
            new WhisperQuotaStatus(40, WhisperQuotaService.UniqueRecipientLimit, false)
        );

        presentation.Text.ShouldBe("40/40");
        presentation.State.ShouldBe(WhisperQuotaPresentationState.Limit);
    }

    [Test]
    public void ExhaustedQuota_Presenting_TreatsQuotaAsAtLimit()
    {
        var presentation = WhisperQuotaPresentation.From(
            new WhisperQuotaStatus(0, WhisperQuotaService.UniqueRecipientLimit, true)
        );

        presentation.Text.ShouldBe("40/40");
        presentation.State.ShouldBe(WhisperQuotaPresentationState.Limit);
    }
}
