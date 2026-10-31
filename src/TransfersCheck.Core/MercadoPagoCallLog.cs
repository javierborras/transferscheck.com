namespace TransfersCheck.Core;

/// <summary>One outgoing HTTP call to Mercado Pago. Not a poll that returned before calling, and not a greeting.</summary>
public sealed class MercadoPagoApiCall
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? TenantId { get; set; }
}

public interface IMercadoPagoCallLog
{
    Task RecordAsync(CancellationToken cancellationToken);
}

/// <summary>Attributes the Mercado Pago calls on this async flow to a commerce. Platform calls leave it unset.</summary>
public static class MercadoPagoCallScope
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? TenantId => Current.Value is { } id && id != Guid.Empty ? id : null;

    public static IDisposable Begin(Guid tenantId)
    {
        var previous = Current.Value;
        Current.Value = tenantId;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
