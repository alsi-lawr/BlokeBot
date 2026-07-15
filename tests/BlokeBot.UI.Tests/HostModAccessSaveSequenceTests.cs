using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostConfig.Page;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.UI.Tests;

public sealed class HostModAccessSaveSequenceTests
{
    [Test]
    public void LaterSubmission_Beginning_CancelsAndSupersedesEarlierSnapshot()
    {
        using var sequence = new HostModAccessSaveSequence();
        var initial = Access(allowModsByDefault: true);
        var first = sequence.Begin(
            Command(HostModeratorAccessMode.FromAllowModsByDefault(false)),
            initial
        );
        var optimisticFirst = Access(allowModsByDefault: false);

        var second = sequence.Begin(
            Command(HostModeratorAccessMode.FromAllowModsByDefault(true)),
            optimisticFirst
        );

        first.CancellationToken.IsCancellationRequested.ShouldBeTrue();
        sequence.IsCurrent(first).ShouldBeFalse();
        sequence.IsCurrent(second).ShouldBeTrue();
        first.PreviousAccess.ShouldBeSameAs(initial);
        second.PreviousAccess.ShouldBeSameAs(optimisticFirst);
        sequence.Complete(first);
        sequence.IsCurrent(second).ShouldBeTrue();
        sequence.HasPendingSubmission.ShouldBeTrue();
        sequence.Complete(second);
        sequence.HasPendingSubmission.ShouldBeFalse();
    }

    [Test]
    public void ActiveSubmission_DisposingSequence_CancelsWithoutFailureOutcome()
    {
        var sequence = new HostModAccessSaveSequence();
        var submission = sequence.Begin(
            Command(HostModeratorAccessMode.FromAllowModsByDefault(false)),
            Access(allowModsByDefault: true)
        );

        sequence.Dispose();

        submission.CancellationToken.IsCancellationRequested.ShouldBeTrue();
        sequence.IsCurrent(submission).ShouldBeFalse();
        sequence.HasPendingSubmission.ShouldBeFalse();
        sequence.Complete(submission);
    }

    private static HostModAccessSaveCommand Command(HostModeratorAccessMode mode)
    {
        return HostModAccessSaveValidator
            .Validate(1, mode)
            .Match(
                command => command,
                errors => throw new InvalidOperationException(errors[0].Message)
            );
    }

    private static HostModAccessState Access(bool allowModsByDefault)
    {
        return new(true, allowModsByDefault, ["allowed"], ["blocked"]);
    }
}
