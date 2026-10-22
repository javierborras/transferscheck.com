using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class MercadoPagoTransferSource : ITransferSource
{
    public const string Code = "mercadopago";
    public string SourceCode => Code;

    public Task<IReadOnlyList<IncomingTransfer>> ReadIncomingAsync(TokenSet tokens, int pageSize, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}

public sealed class UnavailablePushSender : IPushSender
{
    public Task SendArrivedAsync(string token, Guid transferId, string payerName, decimal amount, string currency, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class UnavailableCheckout : ICheckout
{
    public Task<string> CreatePreferenceAsync(Guid tenantId, int amount, string currency, string title, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<string?> ReadApprovedPaymentAsync(string paymentId, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}

public sealed class SqlPollLock(string connectionString) : IPollLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = N'TransfersCheck.Poll',
                @LockMode = N'Exclusive',
                @LockOwner = N'Session',
                @LockTimeout = 0;
            SELECT @result;
            """;
        var result = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? -1);
        if (result < 0)
        {
            await connection.DisposeAsync();
            return null;
        }

        return new HeldLock(connection);
    }

    private sealed class HeldLock(Microsoft.Data.SqlClient.SqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var release = connection.CreateCommand();
                release.CommandText = "EXEC sp_releaseapplock @Resource = N'TransfersCheck.Poll', @LockOwner = N'Session';";
                await release.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
