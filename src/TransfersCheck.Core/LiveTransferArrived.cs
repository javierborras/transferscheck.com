using System.Text.Json;

namespace TransfersCheck.Core;

/// <summary>Shared open-list message. Fixture: contracts/LiveTransferArrived.json.</summary>
public sealed record LiveTransferArrived(
    Guid Id,
    string PayerName,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string Status,
    string StatusLabel)
{
    public const string MessageType = "LiveTransferArrived";
    public const string FixtureFileName = "LiveTransferArrived.json";

    public static LiveTransferArrived FromTransfer(Transfer row) => new(
        row.Id,
        string.IsNullOrWhiteSpace(row.PayerName) ? "Desconocido" : row.PayerName,
        row.Amount,
        row.OccurredAt,
        row.Status,
        StatusWords.Label(row.Status));

    public static void AssertMatchesFixture(string fixtureJson)
    {
        using var doc = JsonDocument.Parse(fixtureJson);
        AssertEqual(MessageType, doc.RootElement.GetProperty("messageType").GetString());
        var fields = doc.RootElement.GetProperty("fields").EnumerateArray().Select(x => x.GetString()).ToArray();
        AssertEqual(new[] { "id", "payerName", "amount", "occurredAt", "status", "statusLabel" }, fields);
    }

    private static void AssertEqual<T>(T expected, T? actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"LiveTransferArrived contract mismatch: expected {expected}, got {actual}.");
    }

    private static void AssertEqual(string[] expected, string?[] actual)
    {
        if (expected.Length != actual.Length || !expected.SequenceEqual(actual))
            throw new InvalidOperationException("LiveTransferArrived fields mismatch.");
    }
}

public interface ILiveListPublisher
{
    Task PublishAsync(Guid tenantId, LiveTransferArrived message, CancellationToken cancellationToken);
    IAsyncEnumerable<LiveTransferArrived> SubscribeAsync(Guid tenantId, CancellationToken cancellationToken);
}
