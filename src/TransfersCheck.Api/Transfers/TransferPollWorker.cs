using Microsoft.Extensions.Options;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Api.Transfers;

public sealed class TransferPollWorker(
    IServiceScopeFactory scopes,
    IOptions<SyncOptions> sync,
    IPollLock pollLock,
    TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(sync.Value.PollInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PollOnce(stoppingToken);
    }

    public async Task PollOnce(CancellationToken cancellationToken)
    {
        var hold = await pollLock.TryAcquireAsync(cancellationToken);
        if (hold is null)
            return;

        await using (hold)
        {
            using var scope = scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
            var transferSync = scope.ServiceProvider.GetRequiredService<TransferSync>();
            foreach (var tenantId in await store.ListConnectedTenantIdsAsync(cancellationToken))
                await transferSync.Run(tenantId, manual: false, cancellationToken);
        }
    }
}
