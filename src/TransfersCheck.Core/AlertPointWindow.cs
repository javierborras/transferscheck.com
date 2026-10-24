namespace TransfersCheck.Core;

/// <summary>How many rows a Punto de Alerta may see (typed Sync:AlertPointRecordCount).</summary>
public static class AlertPointWindow
{
    public static int Take(bool isOwner, int pageSize, int alertPointRecordCount)
        => isOwner ? pageSize : alertPointRecordCount;
}
