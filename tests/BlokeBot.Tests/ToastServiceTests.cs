using BlokeBot.Features.Toasts;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class ToastServiceTests
{
    [Test]
    public void Status_and_success_toasts_auto_dismiss_by_default()
    {
        var service = new ToastService();

        var status = service.Status("Saved.");
        var success = service.Success("Created.");

        status.Tone.ShouldBe(ToastTone.Neutral);
        success.Tone.ShouldBe(ToastTone.Positive);
        status.AutoDismissAfter.ShouldNotBeNull();
        success.AutoDismissAfter.ShouldNotBeNull();
        status.RequiresManualDismiss.ShouldBeFalse();
        success.RequiresManualDismiss.ShouldBeFalse();
    }

    [Test]
    public void Warning_and_error_toasts_require_manual_dismissal_by_default()
    {
        var service = new ToastService();

        var warning = service.Warning("Review this.");
        var error = service.Error("Could not save.");

        warning.Tone.ShouldBe(ToastTone.Caution);
        error.Tone.ShouldBe(ToastTone.Critical);
        warning.AutoDismissAfter.ShouldBeNull();
        error.AutoDismissAfter.ShouldBeNull();
        warning.RequiresManualDismiss.ShouldBeTrue();
        error.RequiresManualDismiss.ShouldBeTrue();
    }

    [Test]
    public void Publish_and_dismiss_update_current_toasts_and_raise_changes()
    {
        var service = new ToastService();
        var changeCount = 0;
        service.Changed += () => changeCount++;

        var toast = service.Publish(ToastKind.Status, "  Bot started.  ");

        toast.Message.ShouldBe("Bot started.");
        service.Current.ShouldHaveSingleItem().ShouldBe(toast);
        changeCount.ShouldBe(1);

        service.Dismiss(toast.Id).ShouldBeTrue();

        service.Current.ShouldBeEmpty();
        changeCount.ShouldBe(2);
    }

    [Test]
    public void Status_toasts_can_override_tone_without_changing_behavior()
    {
        var service = new ToastService();

        var toast = service.Status("Points is now disabled.", "Points disabled", ToastTone.Caution);

        toast.Kind.ShouldBe(ToastKind.Status);
        toast.Tone.ShouldBe(ToastTone.Caution);
        toast.Title.ShouldBe("Points disabled");
        toast.AutoDismissAfter.ShouldNotBeNull();
        toast.RequiresManualDismiss.ShouldBeFalse();
    }
}
