using Shouldly;

namespace BlokeBot.Functional.Tests;

public sealed class IOTests
{
    [Test]
    public void ConstructionAndComposition_BeforeExecution_AreLazy()
    {
        var operationInvocations = 0;
        var mapInvocations = 0;
        var bindInvocations = 0;
        var io = IO<int, TestError>
            .Create(_ =>
            {
                operationInvocations++;
                return ValueTask.FromResult(Result<int, TestError>.Success(21));
            })
            .Map(value =>
            {
                mapInvocations++;
                return value * 2;
            })
            .Bind(value =>
            {
                bindInvocations++;
                return IO<int, TestError>.Create(_ =>
                    ValueTask.FromResult(Result<int, TestError>.Success(value))
                );
            });

        _ = io.ShouldNotBeNull();
        operationInvocations.ShouldBe(0);
        mapInvocations.ShouldBe(0);
        bindInvocations.ShouldBe(0);
    }

    [Test]
    public async Task Success_Executing_ReturnsSuccessResult()
    {
        var io = IO<int, TestError>.Create(static _ =>
            ValueTask.FromResult(Result<int, TestError>.Success(42))
        );

        var result = await io.ExecuteAsync(CancellationToken.None);

        result.Match(static value => value, static _ => 0).ShouldBe(42);
    }

    [Test]
    public async Task Error_Executing_ReturnsErrorResult()
    {
        var expected = new TestError("invalid");
        var io = IO<int, TestError>.Create(_ =>
            ValueTask.FromResult(Result<int, TestError>.Error(expected))
        );

        var result = await io.ExecuteAsync(CancellationToken.None);

        result.Match(_ => new TestError("unexpected"), error => error).ShouldBe(expected);
    }

    [Test]
    public async Task SelectedException_Executing_MapsExpectedException()
    {
        var expected = new ExpectedException("documented");
        ExpectedException? mappedException = null;
        var io = IO<int, TestError>.FromException<ExpectedException>(
            _ => throw expected,
            exception =>
            {
                mappedException = exception;
                return new TestError(exception.Message);
            }
        );

        var result = await io.ExecuteAsync(CancellationToken.None);

        result
            .Match(_ => new TestError("unexpected"), error => error)
            .ShouldBe(new TestError("documented"));
        mappedException.ShouldBe(expected);
    }

    [Test]
    public async Task UnexpectedException_Executing_PreservesInstance()
    {
        var expected = new UnexpectedException();
        ValueTask<int> ThrowUnexpected(CancellationToken _) => throw expected;

        var io = IO<int, TestError>.FromException<ExpectedException>(
            ThrowUnexpected,
            exception => new TestError(exception.Message)
        );

        var thrown = await Should.ThrowAsync<UnexpectedException>(() =>
            io.ExecuteAsync(CancellationToken.None).AsTask()
        );

        thrown.ShouldBe(expected);
    }

    [Test]
    public async Task BroadExceptionMapper_CancellationDuringExecution_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapperInvoked = false;
        var io = IO<int, TestError>.FromException<Exception>(
            token => WaitForCancellationAsync(token, started),
            _ =>
            {
                mapperInvoked = true;
                return new TestError("unexpected");
            }
        );

        var execution = io.ExecuteAsync(cancellation.Token).AsTask();
        await started.Task;
        cancellation.Cancel();

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() => execution);

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        mapperInvoked.ShouldBeFalse();
    }

    [Test]
    public async Task MapAndBind_Executing_RunInCompositionOrder()
    {
        var events = new List<string>();
        var io = IO<int, TestError>
            .Create(_ =>
            {
                events.Add("source");
                return ValueTask.FromResult(Result<int, TestError>.Success(20));
            })
            .Map(value =>
            {
                events.Add("map");
                return value + 1;
            })
            .Bind(value =>
            {
                events.Add("bind");
                return IO<int, TestError>.Create(_ =>
                {
                    events.Add("bound");
                    return ValueTask.FromResult(Result<int, TestError>.Success(value * 2));
                });
            });

        events.ShouldBeEmpty();
        var result = await io.ExecuteAsync(CancellationToken.None);

        events.ShouldBe(["source", "map", "bind", "bound"]);
        result.Match(value => value, _ => 0).ShouldBe(42);
    }

    [Test]
    public async Task Error_MapAndBind_DoNotInvokeInactiveOperations()
    {
        var expected = new TestError("invalid");
        var mapInvoked = false;
        var bindInvoked = false;
        var io = IO<int, TestError>
            .Create(_ => ValueTask.FromResult(Result<int, TestError>.Error(expected)))
            .Map(_ =>
            {
                mapInvoked = true;
                return 42;
            })
            .Bind(_ =>
            {
                bindInvoked = true;
                return IO<int, TestError>.Create(_ =>
                    ValueTask.FromResult(Result<int, TestError>.Success(42))
                );
            });

        var result = await io.ExecuteAsync(CancellationToken.None);

        result.Match(_ => new TestError("unexpected"), error => error).ShouldBe(expected);
        mapInvoked.ShouldBeFalse();
        bindInvoked.ShouldBeFalse();
    }

    [Test]
    public async Task RepeatedExecution_InvokesOperationEachTime()
    {
        var invocations = 0;
        var io = IO<int, TestError>.Create(_ =>
            ValueTask.FromResult(Result<int, TestError>.Success(++invocations))
        );

        var first = await io.ExecuteAsync(CancellationToken.None);
        var second = await io.ExecuteAsync(CancellationToken.None);

        first.Match(value => value, _ => 0).ShouldBe(1);
        second.Match(value => value, _ => 0).ShouldBe(2);
        invocations.ShouldBe(2);
    }

    [Test]
    public async Task CancellationBeforeExecution_DoesNotInvokeOperation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var operationInvocations = 0;
        var io = IO<int, TestError>.Create(_ =>
        {
            operationInvocations++;
            return ValueTask.FromResult(Result<int, TestError>.Success(42));
        });

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() =>
            io.ExecuteAsync(cancellation.Token).AsTask()
        );

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        operationInvocations.ShouldBe(0);
    }

    private static async ValueTask<int> WaitForCancellationAsync(
        CancellationToken cancellationToken,
        TaskCompletionSource started
    )
    {
        var canceled = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var registration = cancellationToken.Register(() =>
            canceled.TrySetCanceled(cancellationToken)
        );
        started.SetResult();
        return await canceled.Task.ConfigureAwait(false);
    }

    private sealed record TestError(string Message);

    private sealed class ExpectedException(string message) : Exception(message);

    private sealed class UnexpectedException : Exception { }
}
