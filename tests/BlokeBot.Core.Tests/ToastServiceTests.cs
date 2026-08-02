using BlokeBot.Core.Features.Toasts;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ToastServiceTests
{
    [Test]
    public void AutomaticToast_PublishingAndDismissing_UsesCustomContentAndNotifiesChanges()
    {
        var service = new ToastService();
        var changeCount = 0;
        service.Changed += () => changeCount++;

        var toast = service.Publish(
            ToastRequest<PositiveStatusToastStrategy>.WithTitle(
                "  Bot started.  ",
                "  Custom bot on  "
            )
        );

        toast.Kind.ShouldBe(ToastKind.Status);
        toast.Tone.ShouldBe(ToastTone.Positive);
        toast.Message.ShouldBe("Bot started.");
        toast.Title.ShouldBe("Custom bot on");
        toast.AutoDismissAfter.ShouldBe(TimeSpan.FromSeconds(4));
        toast.RequiresManualDismiss.ShouldBeFalse();
        service.Current.ShouldHaveSingleItem().ShouldBeSameAs(toast);
        changeCount.ShouldBe(1);

        service.Dismiss(toast.Id).ShouldBeTrue();

        service.Current.ShouldBeEmpty();
        changeCount.ShouldBe(2);
        service.Dismiss(toast.Id).ShouldBeFalse();
        changeCount.ShouldBe(2);
    }

    [Test]
    public void ManualToast_Publishing_UsesDefaultTitleAndRequiresDismissal()
    {
        var service = new ToastService();

        var toast = service.Publish(new ToastRequest<ErrorToastStrategy>("Could not save."));

        toast.Kind.ShouldBe(ToastKind.Error);
        toast.Tone.ShouldBe(ToastTone.Critical);
        toast.Message.ShouldBe("Could not save.");
        toast.Title.ShouldBe("Something went wrong");
        toast.AutoDismissAfter.ShouldBeNull();
        toast.RequiresManualDismiss.ShouldBeTrue();
    }

    [Test]
    public void RequiredContent_ConstructingRequest_RejectsBlankMessageAndTitle()
    {
        Should.Throw<ArgumentException>(() => new ToastRequest<StatusToastStrategy>("  "));
        Should.Throw<ArgumentException>(() =>
            ToastRequest<StatusToastStrategy>.WithTitle("Saved.", "  ")
        );
    }
}
