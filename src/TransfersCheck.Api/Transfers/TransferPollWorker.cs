using Microsoft.Extensions.Options;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Api.Transfers;

public sealed class TransferPollWorker(
    IServiceScopeFactory scopes,
    IOptions<SyncOptions> sync,
    IPollLock pollLock,
    TimeProvider clock,
    ILogger<TransferPollWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(sync.Value.PollInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PollOnce(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed poll must never stop the host; the next tick retries.
                logger.LogError(ex, "Transfer poll tick failed; will retry on the next interval.");
            }
        }
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
            {
                try
                {
                    await transferSync.Run(tenantId, manual: false, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One tenant's bad connection (e.g. revoked Mercado Pago token) must not block the others.
                    logger.LogWarning(ex, "Transfer sync failed for tenant {TenantId}; skipping this tick.", tenantId);
                }
            }
        }
    }
}
