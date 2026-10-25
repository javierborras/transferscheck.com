using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class OwnerSecretRow
{
    public string Card { get; set; } = "";
    public string ValueProtected { get; set; } = "";
    public string LastFour { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EfOwnerSecretStore(
    TransfersCheckDb db,
    ITokenProtector protector,
    IOptions<CheckoutOptions> checkout,
    IOptions<MailOptions> mail,
    IOptions<FirebaseOptions> firebase,
    TimeProvider clock) : IOwnerSecretStore
{
    public async Task<IReadOnlyList<OwnerSecretStatus>> ListAsync(CancellationToken cancellationToken)
    {
        var stored = await db.OwnerSecrets.AsNoTracking().ToDictionaryAsync(row => row.Card, cancellationToken);
        return OwnerServiceCards.All.Select(card => StatusFor(card, stored.GetValueOrDefault(card))).ToList();
    }

    public async Task<string?> GetPlainAsync(string card, CancellationToken cancellationToken)
    {
        var row = await db.OwnerSecrets.AsNoTracking().FirstOrDefaultAsync(item => item.Card == card, cancellationToken);
        if (row is null || string.IsNullOrEmpty(row.ValueProtected))
            return null;
        return protector.Unprotect(row.ValueProtected);
    }

    public async Task SetAsync(string card, string plainSecret, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        if (card == OwnerServiceCards.WhatsApp)
            throw new InvalidOperationException("whatsapp_has_no_key");
        if (!OwnerServiceCards.All.Contains(card))
            throw new ArgumentException("unknown_card", nameof(card));
        if (string.IsNullOrWhiteSpace(plainSecret))
            throw new ArgumentException("secret_required", nameof(plainSecret));

        var trimmed = plainSecret.Trim();
        var lastFour = trimmed.Length <= 4 ? trimmed : trimmed[^4..];
        var existing = await db.OwnerSecrets.FirstOrDefaultAsync(item => item.Card == card, cancellationToken);
        if (existing is null)
        {
            db.OwnerSecrets.Add(new OwnerSecretRow
            {
                Card = card,
                ValueProtected = protector.Protect(trimmed),
                LastFour = lastFour,
                ExpiresAt = expiresAt,
                UpdatedAt = clock.GetUtcNow()
            });
        }
        else
        {
            existing.ValueProtected = protector.Protect(trimmed);
            existing.LastFour = lastFour;
            existing.ExpiresAt = expiresAt;
            existing.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private OwnerSecretStatus StatusFor(string card, OwnerSecretRow? stored)
    {
        if (card == OwnerServiceCards.WhatsApp)
            return new OwnerSecretStatus(card, IsSet: true, LastFour: null, ExpiresAt: null);

        if (stored is not null && !string.IsNullOrEmpty(stored.ValueProtected))
            return new OwnerSecretStatus(card, true, stored.LastFour, stored.ExpiresAt);

        var fromConfig = card switch
        {
            OwnerServiceCards.MercadoPagoPlatform => checkout.Value.AccessToken,
            OwnerServiceCards.Mail => FirstNonEmpty(mail.Value.Password, mail.Value.Host),
            OwnerServiceCards.BrowserPush => FirstNonEmpty(firebase.Value.VapidKey, firebase.Value.ApiKey),
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(fromConfig))
            return new OwnerSecretStatus(card, false, null, null);
        var lastFour = fromConfig.Length <= 4 ? fromConfig : fromConfig[^4..];
        return new OwnerSecretStatus(card, true, lastFour, null);
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}

public sealed class CheckoutAccessTokenSource(
    IOwnerSecretStore secrets,
    IOptions<CheckoutOptions> checkout) : ICheckoutAccessToken
{
    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        var fromOwner = await secrets.GetPlainAsync(OwnerServiceCards.MercadoPagoPlatform, cancellationToken);
        if (!string.IsNullOrWhiteSpace(fromOwner))
            return fromOwner;
        return checkout.Value.AccessToken ?? "";
    }
}
