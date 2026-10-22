# Decisions

Assumptions and non-blocking debt for the TransfersCheck MVP. Locked product behavior stays in `brief.md`, `epics-and-stories.md`, `adrs.md`, `parking-lot.md`, and `mp-authorization-setup.md`. This file does not reopen those. No secret values are recorded here.

## Already done

Verified before implementation. These are facts, not assumptions.

- GitHub remote exists. Owner `javierborras`, repository `transferscheck.com`, visibility private. URL `https://github.com/javierborras/transferscheck.com`. Default branch `main`. Local branch at preflight was `develop`, same commit as `origin/develop` and `origin/main` (`752ac0b`, Initial commit). `gh` is authenticated as `javierborras`.
- SQL Server 2019 Express is installed and running as `localhost\SQLEXPRESS` (15.0.2200.2). Windows integrated authentication as `JAVIDESK\superadmin` succeeds against `master`.
- No connection string, Mercado Pago client id, Mercado Pago client secret, redirect URI, or test-account name was present in this repo, in environment variables, or in a TransfersCheck user-secrets store.
- User-secrets that exist on this machine belong to other applications. They were not copied.
- `Onboarding:MercadoPagoVideoUrl` was unset. The brief already allows an empty URL.
- The installed SDKs at preflight were .NET SDK 9.0.316 and 9.0.319 only. ADR-001 still requires `net10.0`.

## Party — preflight operational data

Question: the human did not supply a SQL connection string, Mercado Pago credentials, a Firebase service-account JSON, or a video URL. What is the minimum that lets the MVP continue without inventing product behavior?

The table closed it, with amendments that do not change the locked stories.

- Mary, John, Sofia, Sally, Winston, Vex, Dana, and Killjoy: close on the minimum below.
- Amelia, Smithy, Grumbal, Boundary, Yui, Wildcard, Level, and Splinter: amend the order and the wording, not the product. Create the catalog, install the SDK before the first `net10.0` build, keep hosts in typed configuration, and label local-only choices as local-only.

Recorded assumption:

- Local development uses SQL Server 2019 Express at `localhost\SQLEXPRESS`, database `TransfersCheck`, compatibility level 150, Windows integrated authentication for the interactive user who runs the process. The connection string lives in user-secrets on `TransfersCheck.Api` (user-secrets id `transferscheck-api`), not in the repo. `TrustServerCertificate=True` is a local Express concession and is not a production setting. EF creates the schema. The database name `TransfersCheck` is a local convention; it is not written in the ADRs. After preflight, that user-secrets store was filled on this machine with the local connection string, a generated session signing key, the data-protection key path, and the mock Mercado Pago and checkout doubles. No real credential was written there.
- The UI dev server uses port 5183 because 5173 was already taken on this machine. `wrangler.toml` describes the Cloudflare Workers static asset. No Cloudflare account was available, so the Workers deploy was not run.
- Mercado Pago and Firebase Cloud Messaging stay on the mock until real credentials exist outside the repo. Real OAuth and real push do not block the rest of the MVP.
- Mock base URLs come from typed configuration. Production hosts are not hardcoded in code.
- `Onboarding:MercadoPagoVideoUrl` stays empty until someone sets it. The player stays hidden. `Conectar Mercado Pago` stays available.
- Installing the .NET 10 SDK is toolchain, not a product decision.

Yui suggested leaving FCM unwired. The build order already requires a mock for FCM, so the mock stands.

## Debt

Non-blocking. Not done.

- Mercado Pago platform client id, client secret, and redirect URI are absent. No test account was named. Real OAuth confirmation (operator checklist step 7) has not been run.
- Firebase service-account JSON was not found in the places checked (environment, this repo, a filename search under `D:\Compartida` that was stopped before a full-disk scan finished). Absence on the whole disk is not proven. The JSON must stay outside the repo. Production push stays blocked until that file is supplied to the API process by path.
- Windows integrated authentication works for this interactive desktop user. It will not work for an API container running as another account. Container deployment needs a connection string supplied outside the image. That string is not invented here.
- `TrustServerCertificate=True` must not be copied into a production connection string.
- Checkout Pro on transferscheck's own Mercado Pago account has no credential in this environment. The mock covers preference creation and the webhook until it exists.
- Root `package.json` in this repo is an untracked Firebase dependency from before this MVP. It is not the PWA. The PWA is `web/`.

## Secret rotation

Do not transcribe the values. Rotate them at the provider before any deploy that would use them. They were not copied into this repo, into user-secrets for TransfersCheck, or into this file.

- `D:\Compartida\SimpleCheckMercadoPago\SimpleCheckMercadoPago\AuthService.cs` contains a hardcoded Mercado Pago client id and client secret. The brief already says proof values are rotated before deploy.
- `D:\Compartida\SimpleCheckMercadoPago\WebSaaS\SimpleCheck.WebAPI\appsettings.json`, `appsettings.Development.json`, and `appsettings.Production.json` contain non-empty client id, client secret, and other credential fields (including a password and an API key). Treat those as proof secrets. Rotate them. Do not reuse them for TransfersCheck.
- A different application's user-secrets store on this machine contains a Mercado Pago access token (`pscieloagenda-pay-api-arca`). It is not a TransfersCheck credential and was not copied.

## Legacy call shape used by the mock

The mock follows calls that already worked in the proof, plus `mp-authorization-setup.md`. It does not copy that application's SaaS shape.

- Authorization URL query uses `response_type=code`, `client_id`, `redirect_uri`, `code_challenge`, `code_challenge_method=S256`, and `state`. The working proof sent `code_challenge_method`, which matches the sample URL in `mp-authorization-setup.md`.
- The same official page's prose also names `code_method`. That disagreement is still open until the contract party records it. The mock must accept the parameter the product sends.
- Token exchange is `POST` form body: `client_id`, `client_secret`, `grant_type=authorization_code`, `code`, `redirect_uri`, `code_verifier`.
- Payment search is `GET /v1/payments/search?sort=date_created&criteria=desc` with the merchant bearer token. The proof kept rows whose `payment_type_id` is `bank_transfer` or `account_money`.
- HTTP 429, or HTTP 400 whose body contains error `2001`, is the rate-limit signal. The product waits until the next round.
- `platform_id=mp` was sent by the proof's web client and omitted by the proof's desktop client. `mp-authorization-setup.md` marks it unverified and not an operator step. The contract party below leaves it off the request.

## Party — unnamed contract details

Question: the locked stories require signup, PKCE, a short-lived session, Checkout Pro, and a word for arrived, and they do not name the fields.

The table did not vote as one voice. Mary, John, Sofia, Sally, Winston, Dana, and Killjoy closed on the bundle below. Amelia, Smithy, Vex, Grumbal, Boundary, Yui, Level, and Splinter amended names or asked that the choices be written as choices. Wildcard preferred a username. The ADRs are not contradicted. These are recorded choices, not text found in the locked docs.

- Login is email and password. Email is required and unique across tenants, because `TenantId` is resolved by the server before a session exists and the client does not choose it. A tenant-scoped email would need a merchant picker. Password is stored as a slow hash. A failed sign-in is one generic refusal. Empty email is refused.
- The signup account is the owner (`IsOwner`). That flag is not a role matrix. The owner creates the other accounts and revokes them. The owner account is not revoked by this action.
- `Session:Lifetime` is typed configuration. The default is 12 hours. "Short-lived" in the ADR did not name 12 hours. This default is the choice. The cookie remains HttpOnly, Secure, and `SameSite=None`.
- Authorization query sends `code_challenge`, `code_challenge_method=S256`, and `state`. It does not send `code_method`. It does not send `platform_id`. The mock accepts that set. If a live application requires the other name, that rename is part of the existing real-OAuth debt.
- The second credential is `Checkout:AccessToken`, typed configuration, user-secrets or the environment, never the repo. It is the Checkout Pro credential on transferscheck's account, not a third merchant secret.
- The webhook extends `AccessUntil` only when the configured shared secret matches, the payment id has not been applied before, and the payment status is approved. A replay does not extend access again. The browser return does not change `AccessUntil`.
- The shared secret travels in the `X-Webhook-Secret` header. The locked docs require a match and do not name the header. Checkout Pro calls the same `MercadoPago:ApiBaseUrl` as the read path, with `Checkout:AccessToken`, so the host stays in configuration and the charging account stays separate.
- Stored status stays the source value. The screen and the push card say Llegó for `approved`, Pendiente for `pending`, and Rechazado for `rejected`. Any other status is shown as the raw value and stays silent.
- Push approval and the other four logins are controls on the last-10 screen for the owner. They are not a new menu and not a new story. The screens stay the ones in the brief.
- Public Firebase web keys, if they ever exist, are typed configuration separate from the service-account JSON. Empty keys mean the client registers a mock token. The service-account JSON stays out of the repo.
- The local UI origin is `http://localhost:5183`. Port 5173 was already taken by another process on this machine. The origin stays in `Cors:UiOrigin` and `Ui:Origin`.
- The last-10 response includes `pollIntervalSeconds` and `buttonCooldownSeconds` from `Sync` so the PWA does not hardcode those intervals. The stories did not name the Spanish words for enter, refresh, pay, revoke, or the extra-login control. The screen uses Entrá, Correo, Contraseña, Actualizar, Pagar, Revocar, Agregar cuenta, and Avisos. Esperá un momento. is the line while the refresh control is waiting. A failed sign-in says El correo o la contraseña no sirven. A failed signup says No se pudo crear la cuenta.
