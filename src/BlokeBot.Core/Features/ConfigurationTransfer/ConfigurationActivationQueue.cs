using System.Threading.Channels;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed class ConfigurationActivationQueue
{
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest }
    );

    public void Wake() => _wake.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        try
        {
            _ = await _wake
                .Reader.ReadAsync(cancellationToken)
                .AsTask()
                .WaitAsync(pollInterval, cancellationToken);
        }
        catch (TimeoutException) { }
    }
}
