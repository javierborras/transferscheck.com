# Epics and stories

Locked MVP, 21 October 2026. Amended 22 October 2026: the UI and the API are two deployables. Amended 22 October 2026 during the parking-lot review. Amended 23 October 2026 from human decisions that day, including the night close of the weekly-cap cutoff and the adopted UI labels. Epics are in build order. Each story is accepted or rejected on its checks. Source is the human decision or the architecture lock of that date, plus the amendment where a story says so. The first bmad-build is the note in `later-research.md`. Epics 11 and 12 stay product decisions and are not that build.

A story is closed only after unit tests in `tests/TransfersCheck.Tests`, Swagger, and the browser. Commit and push happen only when the human asks. This documentation pass does not run that ritual.

New epics 7 through 12 are closed scope from the parking-lot review, amended 23 Oct 2026 where a story says so. Epic 7 follows Epic 2 for a commerce that signs in. Epic 8 follows a Pack Mes payment. Epics 9 and 10 follow the transfer list. Epic 12 follows the plan table in Epic 5. Stories for open items are not in this file. The 500-query recharge is not a story. The transaction-detail reading is a check on story 7.5.

## Amendment — counter shell, 22 October 2026

Human decision. This overrides the same-day party close in `party-viable-product.md` that a menu and a mark are not required. The parking-lot review later the same day reopened the commercial items named at the top of this file.

Previous wording:

- `brief.md` UI rules name the screens as enter, create account, the setup checklist, last 10, and pay.
- `decisions.md` "Party — unnamed contract details" said: "Push approval and the other four logins are controls on the last-10 screen for the owner. They are not a new menu and not a new story. The screens stay the ones in the brief."

Amended 23 Oct 2026, night, from the adopted old-UI salvage. Previous menu words were lista, checklist (Conectar Mercado Pago), configurar usuarios, pagar, and Salir.

The counter screen has a header with the TransfersCheck wordmark and one mark, and a menu: Transacciones, Configurar, Pagos, and Salir on desktop. The menu uses Cerrar sesión for leaving. The bar shows the email, truncated. The slot beside the logo shows `#alias`. Volver on every screen that is not the list returns to the list. Configurar is the commerce admin's settings for Mercado Pago and for this tenant's Puntos de Alerta (agregar cuenta, revocar, and the notification button), including that control on the owner row. It is not a new settings product. The first-run walk stays (story 3.3). Salir, and Cerrar sesión in the menu, end the live session. Login and signup show the wordmark and do not show that menu. Stories 2.2, 2.3, 2.4, and 6.5 keep their checks. The place of the account controls moved off the last-10 screen.

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

As a deployer, I want secrets and intervals in typed configuration, so that credentials stay environment settings and the weekly cap is not guessed.

**Acceptance Criteria:**

**Given** the API configuration
**When** the process starts
**Then** the connection string, Mercado Pago client id and client secret, URLs, redirect, poll interval, button cooldown, page size, JWT key, and Data Protection key path come from appsettings, environment, or user-secrets
**And** page size for the last-10 list is 10
**And** `WeeklyQueryCap` is 500 and `WeeklyQueryWarningGap` is 50
**And** `AlertPointRecordCount` defaults to 10
**And** `EmailCodeLifetime` is 1 hour and `InviteLinkLifetime` is 1 hour
**And** each of those is a typed property, not a new subsystem
**And** plan price and duration are not a single `TrialDays` of 14 or a single `PriceAmount` of 5999

Source: architecture lock 21 Oct 2026. Amended 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026.

### Story 1.5

As a deployer, I want the composition root to map one source to one implementation, so that the read path has a single entry.

Acceptance:

- The ports are `ITransferSource`, `IMerchantAuthorizer`, `IEntitlement`, and `IMerchantStore`.
- The only source entry is `mercadopago`.
- Those four names are the current set, not a closed set: new behavior after wave 1 is a Core use case and a small port, not more methods on `IMerchantStore` and not more static endpoint methods, and this story does not split `IMerchantStore`.

Source: architecture lock 21 Oct 2026

### Story 1.6

As a deployer, I want Hangfire to run two jobs, so that the notice and the referral bonus are not the poll.

**Acceptance Criteria:**

**Given** TransfersCheck.Api
**When** the process starts
**Then** it references Hangfire.AspNetCore 1.8.25 and Hangfire.SqlServer 1.8.25
**And** the dashboard is mapped only in Development, at `/hangfire`
**And** the five-day notice is a Hangfire job
**And** the referral bonus is a Hangfire job
**And** no other Hangfire job is registered
**And** the Mercado Pago poll is still `TransferPollWorker`

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. Already installed. This story registers only those two jobs.

## Epic 2 — Commerce admin and Punto de Alerta

### Story 2.1

As a merchant owner, I want to create an account and sign in, so that I can open the product.

Acceptance:

- Sign-in sets an HttpOnly Secure cookie with a short TTL. The cookie is cross-site (`SameSite=None`) so the Workers UI can send it on credentialed calls to the API.
- The API allow-lists the UI origin for those credentialed calls.
- `TenantId` comes from the server session. The client does not choose it.
- The door is a centered card, one column. It shows "Hola de nuevo", "Inicia sesión en tu cuenta de TransfersCheck", "Dirección de email *", "Contraseña *", "Iniciar sesión", and a show/hide control on the password.
- This door has no Auth0 and no Google sign-in.
- This story is the commerce admin. The email code is story 7.1. The code lives 1 hour. This door copy does not remove that code. This story does not choose the code length.

Source: human decision 21 Oct 2026. Amended 22 Oct 2026. Amended 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. Amended 23 Oct 2026, night. The earlier door words Entrá, Correo, and Contraseña are the previous wording.

### Story 2.2

As a commerce admin, I want the account cap to follow the plan, so that Pack Evento stays one person and Pack Mes stays a small group.

**Acceptance Criteria:**

**Given** Pack Mes
**Then** the commerce has one admin and at most four Punto de Alerta accounts
**And** a fifth Punto de Alerta is refused
**Given** Pack Evento
**Then** the only account is the commerce admin
**And** there is no per-seat price and no permission matrix
**And** the second role is named Punto de Alerta

Amended 22 Oct 2026. Previous place for these controls was the last-10 screen. The admin creates a Punto de Alerta from the menu. The earlier "any paying merchant has five logins" check is withdrawn. How the invite works is story 7.3.

Source: human decision 21 Oct 2026. Amended 22 Oct 2026. Amended 22 Oct 2026 during the parking-lot review.

### Story 2.3

As a person at the counter, I want one live session on my login, so that a second sign-in takes over that account.

Acceptance:

- Each login account has one live session.
- A new login on that same account replaces the previous session.

Amended 22 Oct 2026. Previous acceptance did not name a sign-out control. Amended 23 Oct 2026, night. Salir on desktop, and Cerrar sesión in the menu, end that live session and return to the login screen.

Source: human decision 21 Oct 2026. Amended 22 Oct 2026. Amended 23 Oct 2026, night.

### Story 2.4

As a merchant owner, I want to revoke a login account from the server, so that a former counter person loses access immediately.

Acceptance:

- The revoked session ends.
- The FCM token for that account is deleted, and polling for that device stops.

Amended 22 Oct 2026. Revocar stays on Configurar. The owner account is still not revoked by that action.

Source: human decision 21 Oct 2026. Amended 22 Oct 2026. Amended 23 Oct 2026, night. The screen label Configurar replaces the previous words Configurar usuarios.

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
- The connect words are "Conectar Mercado Pago", "Re-conectar cuenta", the badge "Conectado", and "No conectado". The reconnect confirm is "¿Estás seguro que deseas re-conectar tu cuenta de Mercado Pago?". These words are not the live-channel badge, and they are not the Mercado Pago user id.
- Activate alerts is the existing notification permission, and on iOS Add to Home Screen, on this same checklist.
- The trial line is status. Pay is not a step on the checklist.

Source: human decision 21 Oct 2026. Amended 23 Oct 2026, night.

### Story 3.4

As a merchant owner, I want the server to keep the checklist step, so that the screen follows the connection and the push token rather than a tap I made.

Acceptance:

- The server stores the current step per tenant.
- It sets that step from real state: the account exists, a `MerchantConnection` exists, and a push token exists. The client has no call that advances the step.
- When a `MerchantConnection` exists, the connect step shows done and the last-10 list is reachable.
- The alerts step shows done when a push token exists for the tenant.
- There is no model-interpreted state machine and no chatbot.
- This checklist is the one first-run walk. Later guidance is the help page (story 11.1). There is no chatbot on the connect screen.

Source: human decision 21 Oct 2026. Amended 22 Oct 2026 during the parking-lot review.

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
- The file is not shown to a Punto de Alerta.
- The file contains no secret values.

Source: human decision 21 Oct 2026

## Epic 4 — Last-10 sync

### Story 4.1

As a person at the counter, I want the last 10 incoming transfers kept up to date, so that I can see that the money arrived.

Acceptance:

- A `BackgroundService` polls an entitled merchant. The default interval is 5 minutes, from configuration. Stored types are `bank_transfer` and `account_money`, seeded in a table. The list length is the last 10, and page size in configuration is 10. That length is this counter list and the alert-point window (story 7.5). It is not a cap on the admin history (story 7.6).
- The columns are Fecha, Pagador, Monto, and Estado. There is no Medio column.
- An empty payer name is shown as Desconocido.
- Amounts use es-AR and ARS. Dates use `dd/MM/yyyy HH:mm`.
- There is no Sincronizar control. The poll stays on `TransferPollWorker`. The refresh control in story 4.2 stays.
- Refresh and the amount are large and thumb-reachable on a phone.
- The poll stays. While the webapp is open, story 9.1 shows a new transfer immediately. This story does not remove the poll.
- The closed screen words stay Llegó for approved, Pendiente for pending, and Rechazado for rejected.

Source: human decision 21 Oct 2026. Amended 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026, night.

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

## Epic 5 — Plans and access by date

Amended 22 Oct 2026 during the parking-lot review. Previous checks on stories 5.1, 5.2, and 5.3 are withdrawn: one price, a 14-day trial, a day-15 stop that leaves the list up, Checkout Pro as the payment name, and "no usage cap."

### Story 5.1

As a commerce admin, I want the pay screen to show the active plans, so that I can choose an offer.

**Acceptance Criteria:**

**Given** Pack Evento and Pack Mes are active plans
**When** the admin opens the pay screen
**Then** Pack Evento shows ARS 1000, 24 hours, and the face copy "Un día, un usuario. Ideal para la fiesta o el show."
**And** Pack Mes shows ARS 5999 and 30 days
**And** the screen is not one fixed number
**And** the screen does not offer a quantity pack of 50, 100, 500, or 1000+
**And** the table can hold another plan
**And** Pack Mes has `EnableCopilotHelp` on and Pack Evento has it off
**And** what "ayuda" includes is story 11.3, not a further benefit list
**And** the account row label is "Email"
**And** the active pack label is "Plan"
**And** the screen does not title that card "Suscripción"

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. Amended 23 Oct 2026, night.

### Story 5.2

As a commerce admin, I want 21 days after Mercado Pago is activated and linked on Pack Mes, so that I can watch the list before I pay.

**Acceptance Criteria:**

**Given** signup without Mercado Pago activated and the account linked
**Then** the trial has not started
**When** Mercado Pago is activated and the account is linked on Pack Mes
**Then** the clock starts at that link
**And** access during the trial ends 21 days after that link, at that same clock time
**And** it does not end sometime in day 22
**And** that same link instant is the start of the seven-day usage periods (story 10.1)

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. Amended 23 Oct 2026, night.

### Story 5.3

As a commerce admin, I want to pay the product on the owner's Mercado Pago account, so that the charge stays off the collection account.

**Acceptance Criteria:**

**Given** the admin pays
**Then** the payment is a Mercado Pago payment link
**And** the money is for the SaaS owner's Mercado Pago account set in the services panel
**And** it is not the commerce collection account
**And** there is no preapproval, no automatic debit, and no extra debit layer
**And** the owner does not mark the payment by hand
**And** the browser return does not itself move the access date
**When** they pay while days remain
**Then** the new period continues without a gap
**When** they pay after the block
**Then** the new period starts at payment

Source: human decision 22 Oct 2026 during the parking-lot review.

### Story 5.4

As a commerce admin, I want a warning five days before the access date, so that I can pay while access still works.

**Acceptance Criteria:**

**Given** a plan other than Pack Evento
**When** five days remain before the access date
**Then** an email is sent and the web shows a banner
**And** access still works
**And** new transfers and alerts still run
**And** this notice is a Hangfire job
**And** it is not `TransferPollWorker`
**Given** Pack Evento
**Then** this warning is not sent

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026.

### Story 5.5

As a commerce admin, I want the web to block on the access date, so that use stops until we pay.

**Acceptance Criteria:**

**Given** the access date has been reached and they have not paid
**When** someone opens the web
**Then** the web blocks until they pay
**And** new transfers and alerts stop
**And** for an unpaid Pack Mes trial, that access date is the instant in story 5.2

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026.

### Story 5.6

As the product owner, I want leftover days to stay time, so that nobody is paid back in money.

**Acceptance Criteria:**

**Given** days left on a period
**When** the period ends or they pay again
**Then** those days are not returned as money
**And** there is no cashback
**And** there is no discount coupon

Source: human decision 22 Oct 2026 during the parking-lot review.

### Story 5.7

As a commerce admin, I want Pack Evento to end at 24 hours, so that a one-day offer does not become a month.

**Acceptance Criteria:**

**Given** Pack Evento has started
**When** 24 hours have passed
**Then** access ends under story 5.5
**And** the five-day warning does not apply

Source: human decision 22 Oct 2026 during the parking-lot review.

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

As a commerce admin, I want a Punto de Alerta to receive alerts, and Avisos on my own account, so that arrivals are heard without a permission matrix.

**Acceptance Criteria:**

**Given** the admin's own account
**Then** they can turn notifications on with the button "Habilitar Notificaciones" on Configurar
**And** the button does not name push or Firebase as the mechanism
**And** that control does not deactivate the admin
**Given** a Punto de Alerta account
**Then** that person receives alerts
**And** this story does not add a permission matrix

Amended 22 Oct 2026. Previous behavior left `PushApproved` false on the admin, with no Avisos control on that row. The earlier check that push goes only to accounts the admin has approved is withdrawn for a Punto de Alerta: they receive alerts.

Source: human decision 21 Oct 2026. Amended 22 Oct 2026. Amended 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026, night. The previous words were Avisos from Configurar usuarios.

## Epic 7 — Alias and Punto de Alerta

Build this after Epic 2. The account cap is story 2.2.

### Story 7.1

As a commerce admin, I want every sign-in to require a code sent to my email, so that the password alone does not open the commerce.

**Acceptance Criteria:**

**Given** a commerce admin
**When** they sign in
**Then** the sign-in requires email, password, and a code sent to that same email
**And** a sign-in without that code is refused
**And** the code lives 1 hour
**And** this story does not set the code length

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. The human said the 1-hour lifetime was already defined.

### Story 7.2

As a commerce admin, I want to confirm the commerce alias once, so that the public name never drifts.

**Acceptance Criteria:**

**Given** a new commerce admin
**When** they choose an alias, see it, and confirm it
**Then** it is shown as `#alias`
**And** a later attempt to change it is refused

Source: human decision 22 Oct 2026 during the parking-lot review.

### Story 7.3

As a commerce admin, I want to hand a Punto de Alerta a single-use link, so that I do not share my password.

**Acceptance Criteria:**

**Given** Pack Mes and a confirmed alias
**When** the admin creates a name
**Then** the screen shows `usuario#alias`
**And** the sign-in form is two fields, commerce and user
**And** the hash is not part of a URL
**And** the admin receives a single-use link they can copy or send with `wa.me`
**And** WhatsApp only carries the message
**And** the link lives 1 hour
**And** the link does not work after that hour
**And** single-use stays

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. The human said the 1-hour lifetime was already defined.

### Story 7.4

As a Punto de Alerta, I want to set my password from that link, so that this browser is the one that stays signed in.

**Acceptance Criteria:**

**Given** an unused link
**When** the person opens it and sets a password
**Then** that browser is registered
**And** the link does not work a second time
**When** they sign in on another browser
**Then** the previous session ends
**And** the admin can deactivate the account

Source: human decision 22 Oct 2026 during the parking-lot review.

### Story 7.5

As a Punto de Alerta, I want a short list and the alerts, so that I can confirm a payment without managing the commerce.

**Acceptance Criteria:**

**Given** a signed-in Punto de Alerta
**Then** they see the last N rows
**And** that count is the typed setting `AlertPointRecordCount`, default 10
**And** they may open the detail of one of those rows
**And** they receive alerts
**And** Mercado Pago account features are hidden
**And** they cannot open history, a CSV, the totals, or Mercado Pago account settings
**And** they have no control that manages accounts
**And** they do not see the title "Historial de Transacciones"
**And** an empty payer name on a row is Desconocido

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026, evening. Amended 23 Oct 2026, night. The human adopted the party reading. Mercado Pago sometimes does not show the sender name.

### Story 7.6

As a commerce admin, I want my tenant's history, a CSV, and the totals, so that I can see more than the counter list.

**Acceptance Criteria:**

**Given** a signed-in commerce admin
**Then** they have dominion over their own tenant account
**And** they can query that tenant's transaction history
**And** they can export a CSV report
**And** they can see the grand total of transfers by day, by month, and by a date range
**And** the history title is "Historial de Transacciones"
**And** the columns are Fecha, Pagador, Monto, and Estado
**And** there is no Medio column
**And** there is no search box
**And** this history is not capped at 10 rows

Source: human decision 23 Oct 2026. Amended 23 Oct 2026, evening. Amended 23 Oct 2026, night. The alert point cannot open history, the CSV, or the totals. That check is story 7.5. The 10-row window stays on story 7.5.

## Epic 8 — Referral code

Build this after a Pack Mes payment exists (story 5.3).

### Story 8.1

As a commerce admin, I want a referral code and nothing else, so that an invitation is not a discount.

**Acceptance Criteria:**

**Given** a commerce
**When** someone looks for a way to reduce the price
**Then** the only invitation is a referral code
**And** there is no coupon field
**When** a new commerce pays with that code
**Then** the invited commerce pays the full price
**And** a Pack Evento payment does not grant the bonus
**And** the code is labeled "Mi código"
**And** the screen does not show a cashback balance

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026, night.

### Story 8.2

As a commerce admin inside an active plan, I want my next Pack Mes to be free once, so that one successful invitation is the whole bonus.

**Acceptance Criteria:**

**Given** a referrer inside an active plan
**When** a new commerce pays its first Pack Mes with that code
**Then** the referrer's next Pack Mes period is free, once
**And** that free period continues without a gap
**And** it is not stored as money
**And** granting that bonus is a Hangfire job
**And** it is not `TransferPollWorker`
**When** a second new commerce pays a Pack Mes with the same code
**Then** no second free period is granted

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026.

### Story 8.3

As the product, I want a blocked referrer to stay blocked, so that a referral is not a way back in.

**Acceptance Criteria:**

**Given** a referrer whose access is blocked
**When** a new commerce pays its first Pack Mes with that code
**Then** the referrer stays blocked
**And** the free month is not stored

Source: human decision 22 Oct 2026 during the parking-lot review.

## Epic 9 — Immediate list while the webapp is open

Build this after the transfer list (Epic 4). Do not remove the poll or the browser notification.

### Story 9.1

As a commerce admin, I want a new transfer on the open list at once, so that I do not wait for the next poll while I am looking.

**Acceptance Criteria:**

**Given** the webapp is open
**When** a new transfer is stored
**Then** it appears on the list without waiting for the next poll
**And** the poll still runs
**When** the webapp is closed
**Then** the browser notification still fires
**And** this story names no hub method

Source: human decision 22 Oct 2026 during the parking-lot review.

## Epic 10 — Weekly usage cap

Build this after the transfer list. The recharge pack is not in this epic.

### Story 10.1

As a commerce admin, I want a weekly cap of 500 queries with a warning before the wall, so that a busy week waits instead of billing an overage.

**Acceptance Criteria:**

**Given** `WeeklyQueryCap` is 500 and `WeeklyQueryWarningGap` is 50
**When** 50 queries remain, which is 450 used
**Then** the commerce is warned
**And** the web shows a message
**When** the commerce has reached 500
**Then** new queries and alerts wait until the next week
**And** there is no overage charge
**And** Pack Evento and Pack Mes use the same setting
**And** the weekly-cap cutoff instant is the moment Mercado Pago is linked
**And** from that instant, seven consecutive days run
**And** the next period starts when the previous one ends
**And** it is not a calendar week and not Monday
**And** the 21-day trial uses that same start (story 5.2)
**And** this story does not build an accumulable recharge of 500 queries

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026, evening. Amended 23 Oct 2026, night. The warning-at-450 line is the KISS reading of "500 (con gap de 50 -10%)", a gap before the wall. The seven-day length was adopted that evening. The cutoff instant was closed that night.

## Epic 11 — Help pages

Not in the first bmad-build. The product decisions stay. See `later-research.md`. The index name, the concrete help route, and the deploy pipeline are not in these stories. The Jev contract that the spec already states is ADR-017. Provider names, model thresholds, and the index name stay unset.

### Story 11.1

As a commerce admin, I want the first run once and a help page after that, so that the connect screen stays a checklist.

**Acceptance Criteria:**

**Given** help pages in `web/help/`
**Then** those pages are the corpus a later index reads
**And** `Onboarding:MercadoPagoVideoUrl` is not that corpus
**And** onboarding walks the first-run process once
**And** there is no chatbot on the connect screen

Source: human decision 22 Oct 2026 during the parking-lot review.

### Story 11.2

As a deployer, I want the index refresh to be a separate step, so that a failed refresh does not take production down.

**Acceptance Criteria:**

**Given** a production UI deploy has succeeded
**When** a separate refresh step runs and fails
**Then** production stays up
**And** the previous index remains
**And** the Jev call the spec already states is ADR-017, not a second design in this story
**And** this story does not name an index
**And** this story does not define a deploy pipeline
**And** there is no GitHub Actions pipeline today
**And** this story does not invent one
**And** the hook is that the UI is in production
**And** that pipeline is `later-research.md`, not this build

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026.

### Story 11.3

As a person on a plan, I want help to follow that plan, so that Pack Mes can ask a question and Pack Evento cannot.

**Acceptance Criteria:**

**Given** the plan table
**Then** it has the flag `EnableCopilotHelp`
**And** the human's phrase for that flag was enable-copilot-help
**And** Pack Mes has the flag on
**And** Pack Evento has the flag off
**Given** Pack Mes
**When** the person uses help
**Then** the chatbot is assisted by the AI copilot
**And** they can ask a question
**And** a supported question returns a short explanation, a title, and a link from the help corpus
**And** weak evidence returns a clarification or a way out to the help center
**Given** Pack Evento
**When** the person uses help
**Then** the chatbot is a static menu of predetermined options
**And** a free question is refused
**And** a predetermined option works with no external provider
**And** there is still no chatbot on the connect screen

Source: human decision 23 Oct 2026. The copilot shape is ADR-017. This story does not add a host, a queue, or a provider leader.

## Epic 12 — Owner microsite

Not in the first bmad-build. The product decisions stay. See `later-research.md`. Do not design a host. Build story 12.2 after the plan table (story 5.1) when that research resumes. The helpdesk is not a story. The mail provider value stays unset.

### Story 12.1

As the product owner, I want a separate sign-in from server configuration, so that the public webapp cannot create a superadmin.

**Acceptance Criteria:**

**Given** server configuration supplies the first account
**When** someone opens the owner microsite
**Then** sign-in is email and password issued by TransfersCheck
**And** there is no public signup
**And** Auth0, Google, Cloudflare Access, and any other third party do not perform this login
**And** Mercado Pago OAuth does not open the microsite
**And** this story does not name the configuration keys

Source: human decision 22 Oct 2026 during the parking-lot review.

### Story 12.2

As the product owner, I want to edit plans on the microsite, so that a new price does not require a commerce screen.

**Acceptance Criteria:**

**Given** the owner microsite
**When** the owner edits a plan price or adds a plan
**Then** the commerce pay screen shows the active plans
**And** the commerce webapp has no plan-editing screen

Source: human decision 22 Oct 2026 during the parking-lot review.

### Story 12.3

As the product owner, I want one card per service the product already uses, so that I can replace a secret without seeing the whole value.

**Acceptance Criteria:**

**Given** the services panel
**Then** the cards are the Mercado Pago platform application, mail, browser-push credentials, and WhatsApp
**And** the Mercado Pago card is the platform application, not each commerce connection
**And** the mail card is for the sign-in code and the five-day notice
**And** the WhatsApp card has no key
**And** WhatsApp stays `wa.me` only
**And** each secret card shows whether a secret is set, the last four characters, and an expiry
**And** the full secret is not shown
**And** replacing a secret means pasting it again
**And** there are no free key-value rows
**And** this story adds no card that was not named

Source: human decision 22 Oct 2026 during the parking-lot review.
