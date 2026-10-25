# transferscheck.com

Commerce transfer alerts for Mercado Pago (Argentina).

## Required configuration (secrets stay out of the repo)

Set via environment variables or `dotnet user-secrets` on `TransfersCheck.Api`. Empty placeholders live in `src/TransfersCheck.Api/appsettings.json`.

| Key | Purpose |
|---|---|
| `ConnectionStrings:TransfersCheck` | SQL Server |
| `Session:SigningKey` | ≥32 bytes; API session cookie |
| `Auth0:Authority` / `ClientId` / `ClientSecret` / `RedirectUri` | Commerce Universal Login (ADR-018). Empty Authority keeps the local door for offline tests. |
| `Auth0:LogoutReturnUri` | Allowed logout return URL (falls back to `Ui:Origin`). Must be listed in the Auth0 tenant Allowed Logout URLs. |
| `MercadoPago:*` | Merchant OAuth connect |
| `Checkout:AccessToken` / `WebhookSecret` | SaaS charging account (also replaceable from the owner panel) |
| `Mail:Host` / `FromAddress` (/ `UserName` / `Password`) | When Host and FromAddress are set, five-day notice uses that mail path; otherwise `NullMailSender` |
| `HangfireJobs:FiveDayNoticeCron` / `ReferralBonusCron` | Defaults `0 12 * * *` and `0 */6 * * *` (UTC) |
| `Owner:Email` / `Owner:Password` | Owner microsite door at `/owner` (ADR-014). No public signup. |
| `Firebase:*` | Web push |
| `Cors:UiOrigin` / `Ui:Origin` | Web origin |

## Owner microsite

`/owner` signs in with `Owner:Email` / `Owner:Password` only (not Auth0). The services panel shows whether each named secret is set and its last four characters; paste again to replace. WhatsApp has no key (`wa.me` only). Replacing **Mercado Pago (plataforma)** stores the Checkout access token used for payment links.
