using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class InMemoryLiveListPublisher : ILiveListPublisher
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<LiveTransferArrived>>> _tenants = new();

    public Task PublishAsync(Guid tenantId, LiveTransferArrived message, CancellationToken cancellationToken)
    {
        if (!_tenants.TryGetValue(tenantId, out var readers))
            return Task.CompletedTask;
        foreach (var channel in readers.Values)
            channel.Writer.TryWrite(message);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<LiveTransferArrived> SubscribeAsync(
        Guid tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<LiveTransferArrived>();
        var readers = _tenants.GetOrAdd(tenantId, _ => new ConcurrentDictionary<Guid, Channel<LiveTransferArrived>>());
        readers[id] = channel;
        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var message))
                    yield return message;
            }
        }
        finally
        {
            readers.TryRemove(id, out _);
        }
    }
}
