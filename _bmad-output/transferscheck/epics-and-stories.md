# Epics and stories

Locked MVP, 21 October 2026. Amended 22 October 2026: the UI and the API are two deployables. Epics are in build order. Each story is accepted or rejected on its checks. Source is the human decision or the architecture lock of that date, plus the 22 Oct amendment where a story says so.

## Epic 1 — Platform

### Story 1.1

As a deployer, I want the API and the UI as two deployables of one product, so that the UI runs on Cloudflare Workers and the API stays its own host.

Acceptance:

- TransfersCheck.Api is one Docker image. The image does not serve or contain the PWA.
- `web/` is the Vite React PWA and deploys to Cloudflare Workers.
- The UI and the API share one HTTP contract. The API allow-lists the Workers origin for credentialed browser calls.
- SQL Server 2019 stays outside the API image. The API uses a connection string. EF compatibility level is fixed at 150.

Source: architecture lock 21 Oct 2026. Amended 22 Oct 2026.

### Story 1.2

As a deployer, I want three application projects, so that use cases stay free of EF Core and ASP.NET.

Acceptance:

- TransfersCheck.Core holds entities, use cases, and ports, and does not reference EF Core or ASP.NET.
- TransfersCheck.Infrastructure holds EF Core, Mercado Pago HTTP, and token encryption. TransfersCheck.Api composes the app, issues the local login JWT, and runs the worker. It does not serve the PWA. `web/` is a separate frontend project, not a fourth .NET layer. TransfersCheck.Tests is a test project, not a fourth layer.

Source: architecture lock 21 Oct 2026. Amended 22 Oct 2026.

### Story 1.3

As a merchant, I want my rows separated in the shared database, so that another merchant's transfers never appear on my list.

Acceptance:

- Tenant-owned rows carry `TenantId`, and an EF global filter applies it.
- A read for one merchant returns only that merchant's rows.

Source: architecture lock 21 Oct 2026

### Story 1.4

As a deployer, I want secrets and intervals in typed configuration, so that the commercial numbers and credentials are environment settings.

Acceptance:

- Connection string, Mercado Pago client id and client secret, URLs, redirect, poll interval, button cooldown, page size, JWT key, and Data Protection key path come from appsettings, environment, or user-secrets.
- `TrialDays` is 14, `PriceAmount` is 5999, currency is ARS, and `PaidPeriodDays` is 30. Page size for the MVP is 10.

Source: architecture lock 21 Oct 2026

### Story 1.5

As a deployer, I want the composition root to map one source to one implementation, so that the read path has a single entry.

Acceptance:

- The ports are `ITransferSource`, `IMerchantAuthorizer`, `IEntitlement`, and `IMerchantStore`.
- The only source entry is `mercadopago`.

Source: architecture lock 21 Oct 2026

## Epic 2 — Merchant and 5 logins

### Story 2.1

As a merchant owner, I want to create an account and sign in, so that I can open the product.

Acceptance:

- Sign-in sets an HttpOnly Secure cookie with a short TTL. The cookie is cross-site (`SameSite=None`) so the Workers UI can send it on credentialed calls to the API.
- The API allow-lists the UI origin for those credentialed calls.
- `TenantId` comes from the server session. The client does not choose it.

Source: human decision 21 Oct 2026. Amended 22 Oct 2026.

### Story 2.2

As a merchant owner, I want to create the other login accounts, so that people at the counter can sign in without my Mercado Pago password.

Acceptance:

- A paying merchant has at most five login accounts. The owner is one of them and can create the other four.
- A sixth login account is refused. There is no role matrix and no per-seat price.

Source: human decision 21 Oct 2026

### Story 2.3

As a person at the counter, I want one live session on my login, so that a second sign-in takes over that account.

Acceptance:

- Each login account has one live session.
- A new login on that same account replaces the previous session.

Source: human decision 21 Oct 2026

### Story 2.4

As a merchant owner, I want to revoke a login account from the server, so that a former counter person loses access immediately.

Acceptance:

- The revoked session ends.
- The FCM token for that account is deleted, and polling for that device stops.

Source: human decision 21 Oct 2026

## Epic 3 — Mercado Pago connect

### Story 3.1

As a merchant owner, I want to connect Mercado Pago from the server, so that the app can read incoming transfers while my password stays off the counter device.

Acceptance:

- Connect uses the authorization code flow with PKCE. `state` and the code verifier are stored in a table with an expiry. A row past its expiry is rejected.
- Client id and client secret come from configuration. Values that were in the proof source are rotated before any deploy. Those values are not written in this pack or in the repo.

Source: human decision 21 Oct 2026

### Story 3.2

As a merchant owner, I want my Mercado Pago account linked only to my merchant, so that my transfers stay on my tenant.

Acceptance:

- Linking a Mercado Pago account that is already linked to a merchant is refused.
- OAuth tokens are encrypted at rest. The Data Protection key path comes from configuration.

Source: human decision 21 Oct 2026

### Story 3.3

As a merchant owner, I want a setup checklist in Spanish after signup, so that I connect Mercado Pago before the transfer list is useful.

Acceptance:

- The checklist is voseo Spanish. The order is: create the account, connect Mercado Pago, activate alerts, trial is running.
- Create the account is the existing signup. When the account exists, that step shows done.
- Connect has one primary button and this numbered help beside it: you must manage that Mercado Pago account; tap Connect; sign in and accept; you return here.
- Activate alerts is the existing notification permission, and on iOS Add to Home Screen, on this same checklist.
- The trial line is status. Pay is not a step on the checklist.

Source: human decision 21 Oct 2026

### Story 3.4

As a merchant owner, I want the server to keep the checklist step, so that the screen follows the connection and the push token rather than a tap I made.

Acceptance:

- The server stores the current step per tenant.
- It sets that step from real state: the account exists, a `MerchantConnection` exists, and a push token exists. The client has no call that advances the step.
- When a `MerchantConnection` exists, the connect step shows done and the last-10 list is reachable.
- The alerts step shows done when a push token exists for the tenant.
- There is no model-interpreted state machine and no chatbot.

Source: human decision 21 Oct 2026

### Story 3.5

As a merchant owner, I want to grant access to my Mercado Pago account, so that I never create an application or handle a secret.

Acceptance:

- One platform Mercado Pago application is configured by the operator.
- The merchant never creates a Mercado Pago application, never pastes a client secret, and never uploads a JSON.
- The merchant action is OAuth consent only.

Source: human decision 21 Oct 2026

### Story 3.6

As a merchant owner, I want a video on the connect step, so that I can watch the consent path without the video blocking the button.

Acceptance:

- `Onboarding:MercadoPagoVideoUrl` comes from typed configuration (appsettings, environment, or user-secrets).
- An empty URL hides the player and leaves the numbered steps.
- A non-empty URL shows an embed or a link on the connect step.
- Skipping the video does not block the connect button.
- The player is not a chat, and the recording does not show application creation or a client secret.

Source: human decision 21 Oct 2026

### Story 3.7

As a deployer, I want the one-time Mercado Pago authorization setup written down, so that I can film it and repeat it without putting secrets in the repo.

Acceptance:

- `mp-authorization-setup.md` is the film script and the internal checklist: create the platform application, set the redirect URI, enable PKCE, put the client id and client secret in user-secrets or appsettings, rotate any secret that was in the old proof, and confirm with one real OAuth.
- The file is not shown to cashier accounts.
- The file contains no secret values.

Source: human decision 21 Oct 2026

## Epic 4 — Last-10 sync

### Story 4.1

As a person at the counter, I want the last 10 incoming transfers kept up to date, so that I can see that the money arrived.

Acceptance:

- A `BackgroundService` polls an entitled merchant. The default interval is 5 minutes, from configuration. Stored types are `bank_transfer` and `account_money`, seeded in a table. The list length is the last 10, and page size in configuration is 10.
- The list shows who, amount, when, and status. Refresh and the amount are large and thumb-reachable.

Source: human decision 21 Oct 2026

### Story 4.2

As a person at the counter, I want a refresh button, so that I can check again while the customer is standing there.

Acceptance:

- The button runs the same use case as the clock. The default cooldown is 60 seconds, from configuration.
- When Mercado Pago returns 429 or 2001, that merchant waits until the next round.

Source: human decision 21 Oct 2026

### Story 4.3

As the system, I want each transfer stored once per merchant and source, so that a repeated poll does not duplicate a payment.

Acceptance:

- The unique identity is (`TenantId`, `Source`, `ExternalId`).
- A second insert of the same identity does not create a second row.

Source: human decision 21 Oct 2026

### Story 4.4

As a deployer, I want one worker guarded by a database lock, so that two replicas do not both poll the same merchant.

Acceptance:

- The API process hosts the single worker.
- The worker takes `sp_getapplock` before polling. A replica that does not get the lock skips that poll.

Source: architecture lock 21 Oct 2026

## Epic 5 — Prepaid month and trial

### Story 5.1

As a merchant owner, I want one price on the pay screen, so that I know what the next 30 days cost.

Acceptance:

- The screen shows one number: ARS 5999 every 30 days.
- The number is read from `PriceAmount`, currency, and `PaidPeriodDays`.

Source: human decision 21 Oct 2026

### Story 5.2

As a merchant owner, I want 14 days of use after I connect Mercado Pago, so that I can watch the list before I pay.

Acceptance:

- The trial starts when Mercado Pago is connected. Signup leaves the trial unstarted.
- On day 15, if unpaid, sync and push stop, and the last list stays visible.

Source: human decision 21 Oct 2026

### Story 5.3

As a merchant owner, I want to pay on transferscheck's Mercado Pago account, so that the charge stays separate from the account being read.

Acceptance:

- Checkout Pro uses transferscheck's own Mercado Pago account. The webhook extends `AccessUntil` by 30 days from the current valid end, or from payment time if access has already expired. The browser return leaves `AccessUntil` unchanged.
- There is no usage cap and no preapproval.

Source: human decision 21 Oct 2026

## Epic 6 — Installable PWA and FCM push

### Story 6.1

As a person at the counter, I want to install the app from the browser, so that the list opens like an app on the phone.

Acceptance:

- The client is one React PWA, built from `web/` and deployed to Cloudflare Workers. TransfersCheck.Api does not serve it. The service worker is scoped to the UI origin. Android install is through Chrome.
- On iPhone, push works after Add to Home Screen.

Source: human decision 21 Oct 2026. Amended 22 Oct 2026.

### Story 6.2

As a person at the counter, I want a push after a transfer is stored, so that I hear the arrival.

Acceptance:

- The API sends Firebase Cloud Messaging web push over HTTP v1 after a new transfer is stored.
- Production push traffic goes to FCM HTTP v1. The service-account JSON is absent from the repo, the client, and commits.

Source: human decision 21 Oct 2026

### Story 6.3

As a person at the counter, I want the arrival to show amount, payer, and that it arrived, so that I can answer the customer from the notice.

Acceptance:

- Foreground: a short sound and an on-page notice. Background or locked screen: a system notification with sound. The card shows amount, payer name, and the word for arrived.
- Sound plays for arrived payments. Pending and rejected stay on the list and stay silent.

Source: human decision 21 Oct 2026

### Story 6.4

As a person at the counter, I want one tap on the notification to open that transfer, so that the matching row is the first thing I see.

Acceptance:

- One tap opens the last-10 list with that row first.

Source: human decision 21 Oct 2026

### Story 6.5

As a merchant owner, I want to approve which login accounts receive push, so that only the accounts I choose get alerts.

Acceptance:

- Push is sent only to accounts the owner has approved.
- An account the owner has not approved gets no push.

Source: human decision 21 Oct 2026
