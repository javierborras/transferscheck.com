# Architecture decision records

Locked 21 October 2026. Amended 22 October 2026: the UI and the API are two deployables. Amended again 22 October 2026 during the parking-lot review. Amended 23 October 2026 from human decisions that day, including the night close of the weekly-cap cutoff and the adopted UI labels. One section each: context, decision, consequences. These records match `brief.md`. What is still unknown is `later-research.md`, indexed from `open-decisions.md`. It is not decided here.

## ADR-001 — .NET 10 LTS

Source: architecture lock 21 Oct 2026

### Context

The backend is a small monolith meant to stay on one runtime line. The target framework moniker and the product name need to be the same thing in every backend project file and every document.

### Decision

Use .NET 10 LTS. Every backend project targets `net10.0`. The name of the runtime is .NET 10. The UI is not a .NET project.

### Consequences

TransfersCheck.Core, TransfersCheck.Infrastructure, TransfersCheck.Api, and TransfersCheck.Tests all target `net10.0`. Libraries and language features stay on that line. Documents and project files call it .NET 10. The React PWA does not target `net10.0`.

## ADR-002 — Backend in three projects, UI on Cloudflare Workers, one shared SQL Server 2019 database

Source: architecture lock 21 Oct 2026. Amended 22 Oct 2026. Amended 22 Oct 2026 during the parking-lot review: MediatR and a generic repository stay out; Hangfire and SignalR are no longer exclusions. Hosts, queues, and cloud topology were not redesigned. Amended 23 Oct 2026 so the Hangfire sentence matches ADR-011. Amended 23 Oct 2026, night: the commerce door has no Auth0 and no Google, and production deploy points at `later-research.md`.

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

The backend does not adopt MediatR or a generic repository. The request path stays direct. Auth0 is out, including the commerce login door. That door has no Google sign-in (story 2.1). Hangfire runs the five-day notice and the referral bonus, and the poll stays on `TransferPollWorker` (ADR-011). SignalR runs beside the poll and the browser notification (ADR-010). This review does not add a host, a queue, or a cloud topology. Production deploy is pending research (`later-research.md`). It is not the first bmad-build.

## ADR-003 — Server poll plus the commerce webapp; FCM web push in; React Native out; iOS push requires home-screen install

Source: human decision 21 Oct 2026. Amended 22 Oct 2026. Amended 22 Oct 2026 during the parking-lot review.

### Context

The arrival has to make a sound. A second native codebase is a second product. iPhone web push has a platform limit. The primary client is the webapp on the computer where the commerce already runs stock and collections. The phone is the Pack Evento situation.

### Decision

The server polls. The client is one React webapp, installable, deployed on Cloudflare Workers. TransfersCheck.Api does not serve it. There is no native app and no app-store client. Android install is Chrome. On iPhone, push works after Add to Home Screen.

While that webapp is open, a new transfer also appears immediately (ADR-010). This record does not remove the poll or the browser notification, and it names no hub method.

Push is Firebase Cloud Messaging web push, sent by the API after a new transfer is stored. Production traffic is FCM HTTP v1. Foreground: a short sound and an on-page notice. Background or locked screen: a system notification with sound. The card shows amount, payer name, and the word for arrived. One tap opens the list with that row first. Sound plays for arrived payments. Pending and rejected stay on the list and stay silent. A Punto de Alerta receives alerts (ADR-013). The admin's own Avisos control from the counter-shell decision stays. This record does not add a permission matrix.

The Firebase service-account JSON is never in the repo, never in the client, and never committed.

### Consequences

There is no React Native app and no other native app in this MVP. iPhone Safari inside a browser tab is not a push target; the home-screen install is. The service worker is served with the UI on the Workers origin. The service account is supplied to the API process from outside the repository. The Firebase MCP is not the production push path.

## ADR-004 — Two Mercado Pago credentials; access is a date; payment link; no preapproval

Source: human decision 21 Oct 2026. Amended 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. Amended 23 Oct 2026, night.

### Context

The commerce Mercado Pago account is the account being read. Charging the commerce on that same account would mix the product's money with the customer's transfers. Access is a date. The plan catalog is ADR-009. The earlier single price, the 14-day trial, and the day-15 rule are withdrawn.

### Decision

Two credentials.

1. The commerce connects with OAuth, authorization code plus PKCE, server-side, so the app can read transfers. `state` and the code verifier are rows in a table with an expiry. That connection is the commerce collection account. The product is not paid there.
2. The commerce pays itself through a Mercado Pago payment link. The money lands in the SaaS owner's Mercado Pago account, the one the owner sets in the services panel (ADR-012).

Access is a date. It is prepaid. It is not a credit ledger. The owner does not mark a payment by hand and does not watch a bank account. The browser return does not itself move the date.

Pack Mes has a trial of 21 days. The clock starts when Mercado Pago is activated and the account is linked. Signup does not start it. Access during the trial ends 21 days after that link, at that same clock time. It does not end sometime in day 22.

Five days before the access date, an email and a web banner warn them. Access still works. New transfers and alerts still run. Pack Evento does not get this warning.

On the access date, the web blocks until they pay. New transfers and alerts stop.

If they pay while days remain, the new period continues without a gap. If they pay after the block, the new period starts at payment.

There is no preapproval, no automatic debit, and no extra debit layer. Leftover days are not refundable as money. There is no cashback. There are no discount coupons. The pay screen does not show a cashback balance.

The one on-screen legal sentence, adopted 23 Oct 2026 from the old-UI salvage, is the Aviso Importante: TransfersCheck is an assistance tool, and the user is the only one who verifies that the funds actually landed in the bank or the wallet. The old terms page is not adopted. The old paragraph that treats subscriptions as recurrent and cancellable to avoid future charges is not adopted.

The connect step is one first-run walk, plus an optional video. It is not a chat. Later guidance is the help page (ADR-016).

### Consequences

A missed confirmation is not repaired by the success URL. Client id and client secret live in configuration. The proof had those values in source; they are rotated before any deploy. This record does not contain the values. OAuth tokens are encrypted. There is no preapproval subscription.

The weekly-cap number and what "ayuda" includes were closed on 23 Oct 2026 (ADR-007, ADR-009, ADR-017). This record does not say day 15 and does not say day 22.

## ADR-005 — Transfer identity (`TenantId`, `Source`, `ExternalId`)

Source: human decision 21 Oct 2026

### Context

The poll repeats. The same Mercado Pago payment must stay one row. A later source must be able to exist without colliding with Mercado Pago ids. A Mercado Pago account must not feed two merchants.

### Decision

A transfer's unique identity is (`TenantId`, `Source`, `ExternalId`). One Mercado Pago account is linked to only one merchant. The composition root maps a source code to an implementation. The only entry today is `mercadopago`. Stored incoming types are `bank_transfer` and `account_money`, seeded in a table.

### Consequences

Replaying the same external id does not insert a second row. A second merchant cannot link an already linked Mercado Pago account. A second provider would be a new `Source` value and a new `ITransferSource` implementation. That implementation is out of this MVP. The port stays.

## ADR-006 — One worker plus `sp_getapplock`

Source: architecture lock 21 Oct 2026. Amended 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026.

### Context

The clock and the button are the same read. More than one API replica may run against the shared database. Mercado Pago returned error 2001 when the proof called it again inside a minute. HTTP 429 is the same kind of limit.

### Decision

TransfersCheck.Api hosts one `BackgroundService`. The default poll interval is 5 minutes, from configuration. Manual refresh uses the same use case. The default button cooldown is 60 seconds, from configuration. The worker takes `sp_getapplock` so two replicas do not double-poll. On 429 or 2001, that merchant waits until the next round. The button remains for when the customer is standing at the counter.

### Consequences

The poll stays on `TransferPollWorker`. Hangfire is not this scheduler (ADR-011). This record does not move the poll onto Hangfire. The five-day notice and the referral bonus are the only Hangfire jobs. This record does not add any other job. A replica that does not acquire the lock skips the poll. The lock works because every replica shares one SQL Server database. The button observes the cooldown and the 429/2001 wait. It does not start a tighter retry loop.

## ADR-007 — Typed config, no key-value bag

Source: architecture lock 21 Oct 2026. Amended 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. Amended 23 Oct 2026, evening. Amended 23 Oct 2026, night.

### Context

A key-value configuration bag hides the settings the process needs. Prices and durations live on the plan table (ADR-009). The owner edits those plans on the microsite. A setting that is still unnumbered stays unset. This record does not invent it.

### Decision

Settings that are not plan rows stay typed and come from appsettings, environment, or user-secrets:

- connection string
- Mercado Pago client id and client secret
- URLs and redirect
- poll interval and button cooldown
- page size (the last-10 list is 10)
- JWT key
- Data Protection key path
- currency ARS on the two starting plans
- a weekly query cap, typed as `WeeklyQueryCap`, value 500
- a warning gap, typed as `WeeklyQueryWarningGap`, value 50
- how many records a Punto de Alerta sees, typed as `AlertPointRecordCount`, default 10
- `EmailCodeLifetime` of 1 hour, and `InviteLinkLifetime` of 1 hour

There is no key-value `AppConfiguration` bag. The commerce webapp has no free-settings screen. The owner services panel (ADR-012) is not that bag.

The weekly cap is the same idea for both current plans. `WeeklyQueryCap` is 500 queries. `WeeklyQueryWarningGap` is 50, which is 10 percent. The human's phrase was "500 (con gap de 50 -10%)". The reading used here is a gap before the wall: the commerce is warned when 50 remain, at 450, and at 500 new queries and alerts wait until the next week. The web shows a message. There is no overage charge. The usage week is seven consecutive days. It is not a calendar week and not Monday. The human adopted that length on the evening of 23 Oct 2026.

The weekly-cap cutoff instant is the moment Mercado Pago is linked. From that instant, seven consecutive days run. The next period starts when the previous one ends. The 21-day trial uses that same start (ADR-004). The human closed that instant on the night of 23 Oct 2026.

An accumulable recharge of 500 queries may be evaluated later from real application telemetry. The limit is theoretical. The human believes 500 will be enough. That recharge is not in scope and is not a story.

A single `TrialDays` of 14 and a single `PriceAmount` of 5999 are withdrawn.

### Consequences

A new process setting is a typed property. The weekly cap default is 500. An implementer changes that property. They do not add a subsystem for the cap, the gap, the record count, or the 1-hour lifetimes. The pay screen reads the plan table. The commerce webapp does not gain a business-config screen. It has no Backend Config / Pruebas screen. The services panel stays on the owner microsite (ADR-012), and that microsite is not the first bmad-build (`later-research.md`). The recharge pack stays deferred. It is not a story. The cutoff instant is the Mercado Pago link, above.

## ADR-008 — Account caps, one session each, admin deactivate

Source: human decision 21 Oct 2026. Amended 22 Oct 2026. Amended 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. Amended 23 Oct 2026, evening.

### Context

More than one person may use Pack Mes. They need a login. They must not use the commerce admin's Mercado Pago password. This product is not a store with tills, cajas, or a price per seat. The second role is Punto de Alerta. Sign-in detail is ADR-013.

### Decision

Pack Mes has one commerce admin and up to four Punto de Alerta accounts. Pack Evento has the commerce admin only. A further account is refused. There is no per-seat price.

Each account has one live session. A new sign-in on that account closes the previous one. The admin can deactivate a Punto de Alerta. Deactivation ends that session, deletes its FCM token, and stops polling for that browser.

The session cookie is HttpOnly and Secure, with a short TTL, and `SameSite=None` because the UI origin and the API origin differ (ADR-002). The API issues the local login JWT. `TenantId` comes only from the server session. This record does not choose a new TTL.

There is no permission matrix. The adopted split is ADR-013, including the transaction detail the human adopted on the evening of 23 Oct 2026.

### Consequences

A fifth Punto de Alerta on Pack Mes is refused. Two browsers on one account cannot both stay signed in. Deactivation is immediate and server-side. The invite link and the email sign-in code each live 1 hour. The invite link stays single-use. This record does not define a permission grid or the email-code length.

## ADR-009 — Plan table and the two starting offers

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026.

### Context

The pay screen used to show one number. The commerce now starts from two offers, and the owner must be able to add another later without a code change for each price.

### Decision

Plans live in a table. The owner microsite can add and edit them (ADR-014). The commerce pay screen shows the active plans. The commerce webapp has no plan-editing screen.

Two offers start in the table.

- Pack Evento. ARS 1000. 24 hours. One user, the commerce admin. Face copy: "Un día, un usuario. Ideal para la fiesta o el show." It ends at 24 hours. The five-day warning does not apply (ADR-004). `EnableCopilotHelp` is off. Help is a static menu of predetermined options. The person cannot ask a free question (ADR-017).
- Pack Mes. ARS 5999. 30 days. Up to five accounts: one admin and up to four Puntos de Alerta. The trial is 21 days. The clock starts when Mercado Pago is activated and the account is linked (ADR-004). `EnableCopilotHelp` is on. Help is a chatbot assisted by the AI copilot. The person can ask a question (ADR-017).

The plan table has a flag, `EnableCopilotHelp`. The human's phrase was enable-copilot-help. Pack Mes is on. Pack Evento is off.

Prepaid quantity packs (50, 100, 500, 1000+) are discarded. There is no credit ledger (ADR-004).

### Consequences

A third plan is a row the owner adds, not a new ADR. What "ayuda" includes is this flag and the two help modes, not an undefined benefit list. The weekly cap is 500 queries (ADR-007). Prepaid quantity packs stay discarded. The later 500-query recharge is not one of those packs and is not in scope.

## ADR-010 — SignalR beside the poll and the browser notification

Source: human decision 22 Oct 2026 during the parking-lot review.

### Context

The last 10 transfers stay. The poll stays. The browser notification stays. While the webapp is open, waiting for the next poll is too slow.

### Decision

While the webapp is open, a new transfer appears immediately. When the webapp is closed, the browser notification still fires. The poll still runs. This record names no hub method.

### Consequences

An implementer does not remove `TransferPollWorker` or the push path to make room for this. Hub method names are not decided here.

## ADR-011 — Hangfire runs two jobs; the poll stays on the worker

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026.

### Context

Hangfire.AspNetCore and Hangfire.SqlServer 1.8.25 are already referenced by TransfersCheck.Api. The dashboard is mapped only in Development, at `/hangfire`. The Mercado Pago poll is `TransferPollWorker`.

### Decision

Keep that installation. Register two jobs, and no others. The five-day notice and the referral bonus run on Hangfire. The Mercado Pago poll stays on `TransferPollWorker`. Do not move it.

### Consequences

The dashboard stays a Development route. This record does not add a scheduler design, a cron expression, or any job besides those two.

## ADR-012 — Owner services panel

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026, evening. Amended 23 Oct 2026, night.

### Context

The owner needs to see and replace the secrets the product already uses. A free key-value screen would hide which services exist. The commerce webapp does not get this screen.

### Decision

The owner microsite has one card per service the product already uses. Fields are fixed in code. The owner sees whether a secret is set, the last four characters, and an expiry. The full secret is not shown. Replacing it means pasting again. There are no free key-value rows.

Cards named in this decision:

- Mercado Pago platform application. This is not each commerce connection.
- Mail, for the sign-in code and the five-day notice. The card does not lock SendGrid and does not lock MonsterASP SMTP. One active sender. The choice is a typed setting on that card. The concrete provider value stays unset.
- Browser-push credentials.
- WhatsApp. No key today. `wa.me` only carries a message.

### Consequences

A card that was not named is not added. Commerce OAuth tokens stay on the commerce connection and are not this panel. WhatsApp has no API key to paste. The mail choice does not add a second sender and does not copy SendGrid key names as the only path. Until the microsite screen exists, the same fact is server configuration, not a commerce screen. The concrete mail provider value stays unset. This panel is not the first bmad-build (`later-research.md`). The commerce webapp has no Backend Config / Pruebas screen.

## ADR-013 — Commerce admin, Punto de Alerta, and the alias

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. Amended 23 Oct 2026, evening. Amended 23 Oct 2026, night.

### Context

The commerce admin is the person who connects Mercado Pago. The second person is a Punto de Alerta. This pack does not call that person a cashier and does not talk about cajas. Account caps are ADR-008.

### Decision

Commerce admin. They have dominion over their own tenant account. Every sign-in is email, password, and a code sent to that same email. The code lives 1 hour. The human said that lifetime was already defined as 1 hour. The commerce login door has no Auth0 and no Google sign-in. They choose the commerce alias once, see it, confirm it, and it never changes. It is shown as `#alias`. The slot beside the logo shows that alias. They can query transaction history, export a CSV report, and see the grand total of transfers by day, by month, and by a date range. That history title is Historial de Transacciones. The columns are Fecha, Pagador, Monto, and Estado. There is no Medio column and no search box. That history is not capped at 10 rows. The 10-row window is the alert-point list, not this history. An empty payer name is shown as Desconocido. Amounts use es-AR and ARS. Dates use `dd/MM/yyyy HH:mm`.

Punto de Alerta. The admin creates a name. The screen shows `usuario#alias`. The form is two fields, commerce and user, so the hash is not part of a URL. Entry is a single-use link that lives 1 hour. The human said that lifetime was already defined as 1 hour. Single-use stays as previously closed. The admin copies it or sends it with `wa.me`. WhatsApp only carries the message. The person opens the link, sets a password, and that browser is registered. A later sign-in elsewhere closes the previous one. The admin can deactivate the account. That person sees the last N rows. N is the typed setting `AlertPointRecordCount`, default 10. They may open the detail of one of those rows, because Mercado Pago sometimes does not show the sender name. The row columns are Fecha, Pagador, Monto, and Estado. An empty payer name is Desconocido. Dates use `dd/MM/yyyy HH:mm`. Amounts use es-AR and ARS. They do not get the title Historial de Transacciones. They receive alerts. They cannot open history, a CSV, the totals, or Mercado Pago account settings. They do not manage accounts.

There is no permission matrix. The human adopted this split on the evening of 23 Oct 2026. The party record is `party-permissions-2026-10-23.md`.

### Consequences

The email-code lifetime and the invite-link lifetime are each 1 hour. The email-code length is not chosen. That length is not a research item in `later-research.md`. The alias is immutable after confirmation. A Punto de Alerta has no Mercado Pago account features and no account-management action. They may open the detail of one of the last N rows. They do not get the title Historial de Transacciones. They cannot open history, a CSV, or the totals. Those stay with the commerce admin, by day, by month, and by a date range. The admin history is not capped at 10 rows.

## ADR-014 — Owner microsite sign-in

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026, night.

### Context

The product owner is not a commerce. The commerce webapp must not be the place where plans and service secrets are edited.

### Decision

Superadmin is the product owner. The owner uses a separate microsite. Sign-in is email and password issued by TransfersCheck. The first account comes from server configuration. There is no public superadmin signup. Auth0, Google, Cloudflare Access, and any other third party do not perform this login. Mercado Pago OAuth connects a commerce collection account. It does not open the microsite.

The microsite holds plan and price editing, the services panel (ADR-012), and later the helpdesk. The commerce webapp does not get those screens. The microsite, including this sign-in, plan editing, the services panel, the helpdesk, and the log/PR agent, is pending research. It is not the first bmad-build (`later-research.md`). This record does not design a host.

### Consequences

The helpdesk, and a Cursor agent that reads logs, files bugs, and opens a GitHub pull request for the owner to approve, are decided in intent and are not specified. They stay parked with the microsite helpdesk. They are not an epic. This record does not name the configuration keys for the first account, only that they come from server configuration.

## ADR-015 — Referral code

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026, night.

### Context

A commerce can invite another commerce. The invitation is not a coupon and not cashback.

### Decision

A referral code only. When a new commerce pays its first Pack Mes with that code, the referrer's next Pack Mes period is free, once. That free period continues without a gap, the same prepaid continuation as a payment made while days remain (ADR-004). It is not money.

The referrer must still be inside an active plan. A blocked referrer is not unblocked, and the free month is not stored. A second referral does not grant a second free month. The invited commerce pays the full price. Pack Evento does not grant the bonus.

### Consequences

There is no cashback and no discount coupon (ADR-004). The screen label for the code is Mi código. The screen does not show a cashback balance. The bonus runs on Hangfire (ADR-011). It does not run on `TransferPollWorker`.

## ADR-016 — Help pages are the index corpus

Source: human decision 22 Oct 2026 during the parking-lot review. Amended 23 Oct 2026. Amended 23 Oct 2026, night.

### Context

Help pages stay. They are what a later index reads. The video URL is not that corpus. The help chatbot and the plan flag are ADR-017. This record stays the corpus and the refresh rule.

### Decision

Help pages live in one folder, `web/help/`. Onboarding walks the whole first-run process once. Everything else lives on the help page. There is no chatbot on the connect screen.

After a production UI deploy succeeds, a separate step refreshes the index. If that step fails, production stays up and the previous index remains. The ingest hook is that the UI is in production. This record does not define the pipeline.

`Docs/ManualdeMarcayUI.html` is a reference while designing, for visual, technical, and wording cohesion. It is not a product module and not an approval gate.

### Consequences

The copilot, the plan flag `EnableCopilotHelp`, and the two help modes are ADR-017. This record still does not put a chatbot on the connect screen. The index name is not defined. Hosts, queues, and a deploy pipeline are not defined here. There is no GitHub Actions pipeline today. This record does not invent one. Production pipeline and the post-deploy help ingest are pending research (`later-research.md`). They are not the first bmad-build. Process skills and gates stay out. Verification remains tests, Swagger, and the browser.

## ADR-017 — Help copilot and the plan flag

Source: human decision 23 Oct 2026, plus `help-copilot-technical-spec.md` where that spec decides. Amended 23 Oct 2026, night: implementation waits. The Pscielo.Llm code is evidence of a pattern. Where it disagrees with the spec, the spec is the TransfersCheck intent. This record does not import that product's types, hosts, or secrets.

### Context

What "ayuda" includes was undefined. The human closed it as two chat modes on the plan table. The spec describes a small help copilot. ADR-016 already places the corpus in `web/help/` and keeps the previous index when a refresh fails. ADR-002 already uses one SQL Server 2019 database. The spec mentioned PostgreSQL as a reference and told the receiving agent to resolve that with this product. This record does not add a second database and does not migrate.

### Decision

The plan table flag is `EnableCopilotHelp`. The human's phrase was enable-copilot-help. Pack Mes is on. Pack Evento is off.

Pack Mes. The help chatbot is assisted by the AI copilot. The person can ask a question.

Pack Evento. The help chatbot is a static menu of predetermined options. The person cannot ask a free question. A static option is answered in the app, with no external provider.

The connect screen stays a checklist. It does not gain a chatbot (ADR-016).

The copilot explains how to use the product. It does not run commerce operations, read account data, or take business actions. The person gets a short explanation, the title, and a link to the help that supports it. The link comes from corpus metadata. If the evidence is not enough, the result is a clarification or a way out to the help center. A static option that already has an answer does not call a generative provider. If the evaluator is unavailable, the result is a local unavailability answer and a general help link. Unevaluated text is not generated. A generative failure does not disable the static menu or the help links.

A free question is handled by code in the existing API, in this order. Check access and the input. Apply a deterministic filter for unmistakable bad patterns and invalid input. A word such as "jailbreak" is not by itself a block. If a short conversation context is enabled, include only a bounded slice. Ask Jev to evaluate. Then block, ask for a clarification, answer locally, or search. Retrieve a small set of candidates. Jev may judge whether that evidence is useful. Code selects. A generative provider writes a short summary only when that adds something a stored summary does not already say. Validate the output and attach a catalog link. The browser does not choose the provider, the prompt, or the policy. There is no agent loop over documents and no configurable workflow engine.

Jev is TypeSafe. It answers typed questions about state the application supplies. It does not retrieve documents and it does not write the final summary. The documented call is `POST https://api.typesafe.ai/v1/systemone` with Bearer authentication. The body carries `model`, `state`, and `questions`. The response carries `answers` and usage. One HTTP adapter covers the uses. The spec's proposed uses, turned on only when needed, are Noul, Choice, and Score. A full walk has at most two Jev evaluations, and both are not required every time. Verify that contract when implementing. Do not copy another product's thresholds.

Retrieval uses the simplest text search that fits the existing SQL Server database. Embeddings, a vector store, Redis, a broker, a second service, a flow editor, a provider dashboard, and an agent framework are not added. The corpus is published help in `web/help/` and its summaries (ADR-016). A failed refresh keeps the previous corpus.

Provider selection is explicit. Two or three configurable generative adapters are enough. Normally one generation runs. If a fallback is enabled later, it is a small number of attempts inside one budget. A security block does not switch provider. There is no provider leader, no ranking, no debate, no vote, and no speculative parallel call. Provider names, model ids, order, timeouts, and quotas are not chosen here.

The question and the documents are untrusted data. They are not privileged instructions. Secrets stay on the server. Logs may record the result, timing, call count, provider or model, and corpus or policy version. They do not record full texts or secrets by default.

### Consequences

ADR-016 still owns the folder, the separate refresh, and the rule that a failed refresh leaves production up. This record does not name an index, a cron, a host, or a queue. It does not register a Hangfire job (ADR-011).

Still unset, and not decided here: the index name; which of Noul, Choice, and Score run first, and the thresholds, models, and ambiguity behavior; the generative provider names, model ids, order, timeouts, and quotas; whether fallback is enabled and, if so, the attempt count; whether the first version stores a short conversation context (if it does not, the person must be able to ask a complete question); the concrete HTTP route, JSON names, and status codes behind the conceptual `help-copilot` endpoint. Provider names, model thresholds, and the index name stay unset. This record does not design them. Pack Mes `EnableCopilotHelp` and the Pack Evento static menu stay product decisions. Implementation waits. Those points, and production deploy, are `later-research.md`. They are not the first bmad-build.

Pscielo.Llm shows the pattern and is not the design. A turn there is a prompt walked through steps: a local match can stop the turn before generation, a router then ranks providers by tier and can call a second one in parallel, and help articles are scored from files so a strong match returns the article and a weaker match is handed to a leader step. TransfersCheck keeps the local short-circuit, a small retrieval, and one explicit provider choice. It does not keep that ladder, that leader, that debate, those score cutoffs, or that file-path layout.
