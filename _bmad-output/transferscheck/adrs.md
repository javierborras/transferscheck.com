# Architecture decision records

Locked 21 October 2026. Amended 22 October 2026: the UI and the API are two deployables. One section each: context, decision, consequences. These records match `brief.md`. They do not reopen the product.

## ADR-001 — .NET 10 LTS

Source: architecture lock 21 Oct 2026

### Context

The backend is a small monolith meant to stay on one runtime line. The target framework moniker and the product name need to be the same thing in every backend project file and every document.

### Decision

Use .NET 10 LTS. Every backend project targets `net10.0`. The name of the runtime is .NET 10. The UI is not a .NET project.

### Consequences

TransfersCheck.Core, TransfersCheck.Infrastructure, TransfersCheck.Api, and TransfersCheck.Tests all target `net10.0`. Libraries and language features stay on that line. Documents and project files call it .NET 10. The React PWA does not target `net10.0`.

## ADR-002 — Backend in three projects, UI on Cloudflare Workers, one shared SQL Server 2019 database

Source: architecture lock 21 Oct 2026. Amended 22 Oct 2026.

### Context

The product is one merchant list, one API, and one PWA. They stay one product. A split of the read path, or a database per merchant, adds operating cost for a last-10 read. The UI deploys on Cloudflare Workers, so it cannot be static files inside the API.

### Decision

Two deployables. The backend stays one monolith in three projects:

- TransfersCheck.Core: entities, use cases, and ports. No EF Core. No ASP.NET.
- TransfersCheck.Infrastructure: EF Core, Mercado Pago HTTP, and token encryption.
- TransfersCheck.Api: composition, local login JWT, and the worker. It does not serve the PWA.

TransfersCheck.Tests is a test project, not a layer. `web/` is a separate Vite React PWA project, deployed to Cloudflare Workers. It is not built into the API. The API is its own deployable: one Docker image for the API only. SQL Server 2019 stays outside that image, reached by a connection string, with EF compatibility level fixed at 150. One shared database. Tenant-owned rows carry `TenantId`. An EF global filter applies it. `TenantId` comes only from the server session.

`web/` and TransfersCheck.Api share one explicit HTTP contract. The UI origin and the API origin differ.

Dependency direction: Api references Infrastructure and Core. Infrastructure references Core. Core references neither.

### Consequences

A second API replica shares this database. Isolation is the session plus the global filter. The API image does not contain SQL Server and does not contain the PWA.

The browser calls the API from the Cloudflare Workers origin. That is cross-origin. The API allow-lists the UI origin and accepts credentialed requests. The session stays the API-issued local login JWT in an HttpOnly Secure cookie with a short TTL. The cookie is cross-site (`SameSite=None`; `Secure`) so the browser attaches it on those calls. `TenantId` comes only from the server session. The client does not choose it. This does not add a second login system.

The PWA and its service worker ship with the UI. Their scope is the Workers origin, not the API origin. Push registration stays in the PWA. The API still sends FCM. A notification tap opens the UI. The list data comes from the API contract.

Mercado Pago still redirects to the API's static https callback. After the server stores the connection, the browser continues on the UI.

The backend does not adopt MediatR, a generic repository, Hangfire, Auth0, or SignalR.

## ADR-003 — Server poll plus PWA; FCM web push in; React Native out; iOS push requires home-screen install

Source: human decision 21 Oct 2026. Amended 22 Oct 2026.

### Context

The person at the counter uses a phone. The arrival has to make a sound. A second native codebase is a second product. iPhone web push has a platform limit.

### Decision

The server polls. The client is one React PWA, installable, deployed on Cloudflare Workers. TransfersCheck.Api does not serve it. Android install is Chrome. On iPhone, push works after Add to Home Screen.

Push is Firebase Cloud Messaging web push, sent by the API after a new transfer is stored. Production traffic is FCM HTTP v1. Foreground: a short sound and an on-page notice. Background or locked screen: a system notification with sound. The card shows amount, payer name, and the word for arrived. One tap opens the list with that row first. Sound plays for arrived payments. Pending and rejected stay on the list and stay silent. The owner approves which accounts receive push.

The Firebase service-account JSON is never in the repo, never in the client, and never committed.

### Consequences

There is no React Native app and no other native app in this MVP. iPhone Safari inside a browser tab is not a push target; the home-screen install is. The service worker is served with the UI on the Workers origin. The service account is supplied to the API process from outside the repository. The Firebase MCP is not the production push path.

## ADR-004 — Two Mercado Pago credentials; trial and Checkout Pro write AccessUntil; no preapproval

Source: human decision 21 Oct 2026

### Context

The merchant's Mercado Pago account is the account being read. Charging the merchant on that same account would mix the product's money with the customer's transfers. Trial and payment need one clock, `AccessUntil`.

### Decision

Two credentials.

1. The merchant connects with OAuth, authorization code plus PKCE, server-side, so the app can read transfers. `state` and the code verifier are rows in a table with an expiry.
2. Checkout Pro runs on transferscheck's own Mercado Pago account.

The trial is 14 days and starts when Mercado Pago is connected. Signup leaves the trial unstarted. On day 15, if unpaid, sync and push stop. The last list stays visible.

The webhook extends `AccessUntil` by 30 days from the current valid end, or from payment time if access has already expired. The browser return leaves `AccessUntil` unchanged.

The price is ARS 5999 every 30 days, one number on screen, from configuration. There is no preapproval, no plan table, no usage cap, and no 500-transaction weekly limit.

The connect step is a checklist plus an optional video, not a chat.

### Consequences

A missed webhook is not repaired by the success URL. Client id and client secret live in configuration. The proof had those values in source; they are rotated before any deploy. This record does not contain the values. OAuth tokens are encrypted. There is no preapproval subscription and no plan catalog to maintain.

## ADR-005 — Transfer identity (`TenantId`, `Source`, `ExternalId`)

Source: human decision 21 Oct 2026

### Context

The poll repeats. The same Mercado Pago payment must stay one row. A later source must be able to exist without colliding with Mercado Pago ids. A Mercado Pago account must not feed two merchants.

### Decision

A transfer's unique identity is (`TenantId`, `Source`, `ExternalId`). One Mercado Pago account is linked to only one merchant. The composition root maps a source code to an implementation. The only entry today is `mercadopago`. Stored incoming types are `bank_transfer` and `account_money`, seeded in a table.

### Consequences

Replaying the same external id does not insert a second row. A second merchant cannot link an already linked Mercado Pago account. A second provider would be a new `Source` value and a new `ITransferSource` implementation. That implementation is out of this MVP. The port stays.

## ADR-006 — One worker plus `sp_getapplock`

Source: architecture lock 21 Oct 2026

### Context

The clock and the button are the same read. More than one API replica may run against the shared database. Mercado Pago returned error 2001 when the proof called it again inside a minute. HTTP 429 is the same kind of limit.

### Decision

TransfersCheck.Api hosts one `BackgroundService`. The default poll interval is 5 minutes, from configuration. Manual refresh uses the same use case. The default button cooldown is 60 seconds, from configuration. The worker takes `sp_getapplock` so two replicas do not double-poll. On 429 or 2001, that merchant waits until the next round. The button remains for when the customer is standing at the counter.

### Consequences

There is no Hangfire and no second scheduler. A replica that does not acquire the lock skips the poll. The lock works because every replica shares one SQL Server database. The button observes the cooldown and the 429/2001 wait. It does not start a tighter retry loop.

## ADR-007 — Typed config, no key-value bag

Source: architecture lock 21 Oct 2026

### Context

A key-value configuration bag hides the settings the process needs and invites an admin screen. The commercial number has to change by configuration, and for this MVP it is 5999.

### Decision

Settings are typed and come from appsettings, environment, or user-secrets:

- connection string
- Mercado Pago client id and client secret
- URLs and redirect
- poll interval and button cooldown
- page size (MVP value 10)
- JWT key
- Data Protection key path
- `TrialDays` = 14
- `PriceAmount` = 5999
- currency = ARS
- `PaidPeriodDays` = 30

There is no key-value `AppConfiguration` bag.

### Consequences

A new setting is a typed property plus a deploy or an environment change. The pay screen reads the configured price. The commercial number for this MVP is 5999. A business-config admin UI is out of the MVP.

## ADR-008 — Five login accounts, one session each, owner revoke

Source: human decision 21 Oct 2026. Amended 22 Oct 2026.

### Context

More than one person stands at the counter. They need a login. They must not use the owner's Mercado Pago password. This product is not a store with roles, tills, and a price per seat.

### Decision

A paying merchant has up to five login accounts. The owner is one of the five and can create the other four. Each login account has one live session. A new login on that account replaces the previous session. The owner can revoke an account from the server: the session ends, the FCM token is deleted, and polling for that device stops.

The session cookie is HttpOnly and Secure, with a short TTL, and `SameSite=None` because the UI origin and the API origin differ (ADR-002). The API issues the local login JWT. `TenantId` comes only from the server session.

There is no role matrix, no 5-PoS admin, and no per-seat price.

### Consequences

A sixth account is refused. Two phones on one login cannot both stay signed in. Revoke is immediate and server-side. Push goes only to accounts the owner has approved. There is no permission grid.
