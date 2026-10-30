namespace TransfersCheck.Core;

public static class AlertPointAccess
{
    public const int AdditionalPrice = 1000;
    public static int Included(string? planCode) => planCode == PlanCodes.PackMes ? 4 : planCode == PlanCodes.PackCorporativo ? 5 : 1;
    public static bool IsIncluded(LoginAccount account, Merchant merchant)
        => account.AlertPointPosition > 0 && account.AlertPointPosition <= Included(merchant.PlanCode);
    public static bool Allows(LoginAccount account, Merchant? merchant, DateTimeOffset now)
        => !account.Revoked && merchant?.AccessUntil > now
            && (account.IsOwner || IsIncluded(account, merchant) || account.ExtraAccessUntil > now);
}

public static class PollingCadence
{
    public static TimeSpan Interval(DateTimeOffset? lastInteraction, DateTimeOffset now, SyncOptions options)
    {
        var idle = lastInteraction is { } at ? now - at : TimeSpan.MaxValue;
        return idle >= TimeSpan.FromMinutes(10) ? options.IdlePollInterval
            : idle >= TimeSpan.FromMinutes(2) ? options.QuietPollInterval : options.PollInterval;
    }
}
