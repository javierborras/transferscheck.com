using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

/// <summary>
/// Writes one row per outgoing Mercado Pago HTTP call, on its own connection,
/// so a sync transaction cannot roll the count back after the call already left.
/// </summary>
public sealed class EfMercadoPagoCallLog(IServiceScopeFactory scopes, TimeProvider clock, ILogger<EfMercadoPagoCallLog> logger) : IMercadoPagoCallLog
{
    public async Task RecordAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
            db.MercadoPagoApiCalls.Add(new MercadoPagoApiCall
            {
                OccurredAt = clock.GetUtcNow(),
                TenantId = MercadoPagoCallScope.TenantId
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // The Mercado Pago call still goes out. A failed note must not block it.
            logger.LogWarning("No se pudo anotar la consulta a Mercado Pago ({ExceptionType}).", exception.GetType().Name);
        }
    }
}
