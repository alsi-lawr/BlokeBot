using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlokeBot.Core.Tests;

internal sealed class PointTransactionBarrier : DbTransactionInterceptor
{
    private int _armed;
    private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Entered => _entered.Task;

    internal void Arm()
    {
        _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _armed = 1;
    }

    internal void Release() => _release.SetResult();

    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default
    )
    {
        if (Interlocked.Exchange(ref _armed, 0) == 1)
        {
            _entered.SetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
        return result;
    }
}
