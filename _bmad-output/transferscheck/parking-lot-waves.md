# Parking-lot waves

Superseded 22 October 2026 by the parking-lot review. See `decisions.md`, `adrs.md`, and `open-decisions.md`. Do not treat the commercial locks in this file as current.

Status: draft for human review. Written 22 October 2026. No application code, no commit, no push.

This file classifies every item named in `parking-lot.md`. It is the review copy. It is not a `plan-*.md` feature plan, and it has no `status` frontmatter that `bmad-build` can resume into implementation.

Baseline inspected: branch `mvp/transferscheck`, commit `155f648d2185c289a81b7fc94d169d69520880b5`. The working tree was dirty. This file does not treat those uncommitted edits as a new product decision. See Assumptions.

## How to run with bmad-build

1. A human verifies this file first.
2. One wave per `bmad-build` invocation. Do not start Wave N+1 until Wave N's close ritual has passed.
3. There is no Wave 1 in this revision. Do not invoke `bmad-build` to implement this file.
4. A wave appears here only after the human amends the blocking ADR or decision named under that item, and asks for this file to be revised. Amending those records is not part of this plan.
5. Commit and push are the recorded close of a wave. They happen only when the human asks for them during that later implementation.

## Target repository

Waves, if a later revision adds any, land in:

`D:\Repo\transferscheck.com\transferscheck.com`

That repo is the product. The API is `src/TransfersCheck.Api`, `src/TransfersCheck.Core`, and `src/TransfersCheck.Infrastructure`. The UI is `web/`. Tests are `tests/TransfersCheck.Tests`. Swagger is the TransfersCheck document at `/swagger`.

`D:\Compartida\SimpleCheckMercadoPago` is the earlier proof. `decisions.md` ("Secret rotation" and "Legacy call shape used by the mock") says the mock follows calls that already worked in the proof and does not copy that application's SaaS shape, and that proof secrets are rotated and not reused. That tree already contains `Coupon`, `Subscription`, and `AppConfiguration` (`WebSaaS/SimpleCheck.Core/Entities/`). Those types are the shapes this plan refuses. No wave lands there.

## What already exists

The locked MVP surface is already in this repo. Parking-lot work is not a greenfield app.

- Typed commercial clock: `CommercialOptions` is `TrialDays` 14, `PriceAmount` 5999, currency ARS, `PaidPeriodDays` 30 (`src/TransfersCheck.Core/Options.cs`). `Entitlement` starts a trial and extends `AccessUntil` by days (`src/TransfersCheck.Core/Ports.cs`).
- One transfer source: `ITransferSource` is registered once, as `MercadoPagoTransferSource` (`src/TransfersCheck.Infrastructure/DependencyInjection.cs`). Story 1.5 requires that single entry.
- Local session: account and session routes under `/api` in `AccountEndpoints`. ADR-008 is the five-login model.
- Current HTTP surface, already in Swagger: `POST /api/accounts`, `POST /api/sessions`, `DELETE /api/sessions`, `GET /api/session`, `GET /api/logins`, `POST /api/logins`, `DELETE /api/logins/{id}`, `PUT /api/logins/{id}/push`, `POST /api/connections/mercadopago`, `GET /api/connections/mercadopago/callback`, `GET /api/checklist`, `GET /api/billing`, `POST /api/billing/checkout`, `POST /api/billing/webhook`, `GET /api/billing/return`, `GET /api/transfers`, `POST /api/transfers/refresh`, `GET /api/push/config`, `POST /api/push/tokens`.
- Current desktop UI, already in `web/src/App.tsx` on the dirty tree: enter, create account, list, checklist (`/conectar`), Configurar usuarios (`/usuarios`), pay (`/pagar`), Volver, Salir, and one wordmark. Configurar usuarios is the five-login screen from the 22 October counter-shell decision. It is not the business-config admin UI.
- Tests already present: `tests/TransfersCheck.Tests` (xUnit, `net10.0`), one class per epic (`Epic1PlatformTests` through `Epic6PushTests`).

`parking-lot.md` also says the static checklist and the video are in the MVP, specified in `brief.md` and `mp-authorization-setup.md`, and are not items to pull from the parking lot. They stay there. `OnboardingOptions.MercadoPagoVideoUrl` is the typed URL.

## Wave list

No wave is scheduled.

| ID | Goal | Parking-lot items |
| --- | --- | --- |
| — | None. Every named item contradicts a locked ADR or a locked decision. A spike for the store product, native apps, or an LLM would still specify a forbidden product, so no spike wave is opened either. | — |

There is nothing to split or merge. A shippable slice would require the amendment named on the item before any API, UI, or database work.

## Do not schedule

Each item is in this bucket only. Quotes are from the locked files. "What must change first" is the amendment. This plan does not make that amendment.

### PL-01 — 24-hour day pass

- **Blocking decision:** ADR-004 and the brief's commercial table. ADR-004: "The trial is 14 days and starts when Mercado Pago is connected." "The webhook extends `AccessUntil` by 30 days from the current valid end, or from payment time if access has already expired." `brief.md` "Commercial terms" locks trial start, day 15, and a 30-day extension. `parking-lot.md`: "Access is the 14-day trial and the 30-day prepaid period." Stories 5.2 and 5.3 accept that clock. `Entitlement` adds days only.
- **What must change first:** Amend ADR-004, `brief.md` "Commercial terms", and stories 5.2 and 5.3 so a 24-hour product exists beside the 14-day trial and the 30-day period. Then remove the day-pass line from `parking-lot.md`.

### PL-02 — Prepaid packs of 50, 100, 500, and more than 1000

- **Blocking decision:** ADR-004: "The price is ARS 5999 every 30 days, one number on screen, from configuration." Story 5.1: "The screen shows one number: ARS 5999 every 30 days." `brief.md` executive summary: "The owner pays one price, ARS 5999 every 30 days." `parking-lot.md`: "The offer is one price, ARS 5999 every 30 days." `decisions.md` "Human decision — counter shell, 22 October 2026": "`parking-lot.md` stays closed: no brand manual, no assistant, no native app, no packs, no help CMS."
- **What must change first:** Amend ADR-004, story 5.1, and the brief's commercial table to allow more than one offer. Then amend the 22 October counter-shell sentence and the parking-lot line.

### PL-03 — Merchant credit ledger (cuenta corriente)

- **Blocking decision:** `parking-lot.md`: "There is no ledger." ADR-004's access clock is `AccessUntil`, not a balance. Story 5.3 extends that date by 30 days. `IEntitlement.Extend` implements that date. `decisions.md` says locked product behavior stays in `parking-lot.md` and `adrs.md`.
- **What must change first:** Amend ADR-004 so a balance replaces or sits beside `AccessUntil`, and amend story 5.3 and the brief's commercial table. Then remove the ledger line from `parking-lot.md`.

### PL-04 — Store product

Internal point-of-sale admin, 1000 transactions a week, five points of sale, a role matrix, and a per-seat price.

- **Blocking decision:** ADR-008: "There is no role matrix, no 5-PoS admin, and no per-seat price." Story 2.2: "There is no role matrix and no per-seat price." `brief.md` "Who This Serves": "This is one merchant with a shared list. It has no role matrix and no per-seat price." `parking-lot.md`: "transferscheck is five logins on one merchant, not that product."
- **What must change first:** Amend ADR-008, story 2.2, and `brief.md` "Who This Serves" to define a different product. A spike is not a substitute for that amendment. The weekly transaction count in this item also collides with PL-07.

### PL-05 — A second payment provider already connected

- **Blocking decision:** ADR-005: "The only entry today is `mercadopago`." "A second provider would be a new `Source` value and a new `ITransferSource` implementation. That implementation is out of this MVP. The port stays." Story 1.5: "The only source entry is `mercadopago`." The composition root registers one `ITransferSource`.
- **What must change first:** Amend ADR-005 and story 1.5 to name the second source and to bring that implementation into scope. The existing port is not permission to build it.

### PL-06 — Native apps

- **Blocking decision:** ADR-003: "The client is one React PWA, installable, deployed on Cloudflare Workers." "There is no React Native app and no other native app in this MVP." Story 6.1 accepts that client. `parking-lot.md`: "The client is the React PWA. React Native and other native apps stay out." The 22 October counter-shell sentence says the parking lot stays closed on native apps. iPhone push stays Add to Home Screen (ADR-003).
- **What must change first:** Amend ADR-003 and story 6.1 so a second client is in the product. A spike is not a substitute.

### PL-07 — The 500-transaction weekly cap

- **Blocking decision:** ADR-004: "There is no preapproval, no plan table, no usage cap, and no 500-transaction weekly limit." Story 5.3: "There is no usage cap and no preapproval." `brief.md` "Commercial terms": "Usage | No usage cap." `parking-lot.md`: "There is no usage cap."
- **What must change first:** Amend ADR-004, story 5.3, and the brief's usage row to define a cap and what happens when it is reached.

### PL-08 — LLM support, including System One

- **Blocking decision:** `parking-lot.md` lists "LLM support, including System One" under items that are "Explicitly out of the MVP" and says "Do not treat this list as a backlog to pull forward." The 22 October counter-shell sentence closes the parking lot on "no assistant". The pack never defines System One. The connect path is separately closed by PL-10.
- **What must change first:** Define what System One is, outside this plan, and amend `parking-lot.md` to remove the exclusion. If the support bot is the connect chat, PL-10's amendment is also required. This plan does not invent the missing definition.

### PL-09 — Auto-published help, a help publisher, and an FAQ CMS

- **Blocking decision:** `parking-lot.md`: "The onboarding video is one typed URL, `Onboarding:MercadoPagoVideoUrl`, not a CMS entry." Story 3.6 and `brief.md` "Setup checklist" specify that single URL. The 22 October counter-shell sentence says the parking lot stays closed on "no help CMS." ADR-007 forbids a key-value bag for settings; a CMS of help entries would be a content store the typed URL does not allow.
- **What must change first:** Amend story 3.6, the brief's setup checklist, and `parking-lot.md` so help is more than one typed URL. If entries are stored as free-form settings, amend ADR-007 as well.

### PL-10 — Semantic onboarding assistant

Jev, RAG, multi-provider chat, and a tool-calling bot.

- **Blocking decision:** ADR-004: "The connect step is a checklist plus an optional video, not a chat." Story 3.4: "There is no model-interpreted state machine and no chatbot." `parking-lot.md`: "The human rejected that assistant on 21 Oct 2026." `party-viable-product.md` "What closed": "Onboarding beyond the checklist is not required."
- **What must change first:** Amend ADR-004 and story 3.4 to allow a chat on the connect step, and reverse the 21 October rejection recorded in `parking-lot.md`.

### PL-11 — Coupons, referrals, and cashback

- **Blocking decision:** ADR-004 and story 5.1 lock one price, ARS 5999, from configuration. ADR-004: "There is no preapproval, no plan table." A coupon is a second price. Referrals and cashback are a balance, which PL-03 keeps out. `parking-lot.md` lists all three under "Commercial extras." The proof's `Coupon` entity is not a source to port (`decisions.md`, "Legacy call shape used by the mock").
- **What must change first:** Amend ADR-004, story 5.1, and the brief's commercial table so the charged amount can change. Referrals or cashback also need the PL-03 amendment.

### PL-12 — Auth0

- **Status (24 Oct 2026):** Unparked for **commerce** identity. ADR-018, amended ADR-002 / ADR-008 / ADR-013, stories 2.1 and 7.1. Session cookie after Auth0 still API-issued. Owner microsite remains out of Auth0 (ADR-014). Punto de Alerta remains usuario/clave.
- **Previous blocking decision (history):** ADR-002 used to say Auth0 was out of the commerce door; the 23 Oct night leave said no Auth0 and no Google on that door. Those locks are withdrawn.
- **What must change next:** Realign implementation and tests (wave 4 / wave 16 runbook, `web/e2e/door.spec.ts`) to Auth0 Universal Login + Database + Social (Google default). Do not put Auth0 on the owner microsite or on the Punto de Alerta door.

### PL-13 — Business-config admin UI

- **Blocking decision:** ADR-007: "Settings are typed and come from appsettings, environment, or user-secrets." "There is no key-value `AppConfiguration` bag." "A business-config admin UI is out of the MVP." "A new setting is a typed property plus a deploy or an environment change." `parking-lot.md`: "Intervals, price, and secrets stay in typed configuration."
- **What must change first:** Amend ADR-007 so price, intervals, and secrets can be edited at runtime. The screen already named Configurar usuarios stays the five-login screen (stories 2.2, 2.4, and 6.5). It is not this item.

### PL-14 — Brand manual

- **Blocking decision:** `parking-lot.md`: "UI copy for this MVP is the UI rules paragraph and the setup checklist in `brief.md`. A separate brand manual stays out." The 22 October counter-shell sentence repeats that the parking lot stays closed on "no brand manual," and then specifies the header that is in scope: "a header with the word TransfersCheck and one mark." That mark is the counter-shell decision already reflected in the dirty `web/` tree. It is not a brand manual.
- **What must change first:** Amend `parking-lot.md` "This pack" and the 22 October sentence that keeps the brand manual closed.

### PL-15 — Skills and process gates

- **Blocking decision:** `parking-lot.md`: "TransfersCheck.Tests remains the test project named in ADR-002. Skill files and extra gate documents are out of this pack." ADR-002 names `TransfersCheck.Tests` as the test project, not a fourth layer. The agent skill files already in the repo are tooling for this planning method. They are not a product feature to extend.
- **What must change first:** Amend `parking-lot.md` "This pack" before any new gate document is added to the product pack. ADR-002 changes only if the test project stops being `TransfersCheck.Tests`. This item has no Swagger or browser surface.

### Architecture exclusions named in `parking-lot.md`

`parking-lot.md` ends: "Architecture exclusions that are already decided, and are not queued work, live in `adrs.md`: MediatR, a generic repository, Hangfire, SignalR, a key-value configuration bag, plan tables, and preapproval." They are classified here so they are not dropped. `party-viable-product.md` section 3 repeats the same list and says "Do not pull these in."

### PL-16 — MediatR

- **Blocking decision:** ADR-002: "The backend does not adopt MediatR, a generic repository, Hangfire, Auth0, or SignalR." Endpoints call use cases directly.
- **What must change first:** Amend ADR-002.

### PL-17 — Generic repository

- **Blocking decision:** ADR-002, same sentence as PL-16. Persistence is `IMerchantStore` / `EfMerchantStore`, a merchant store, not a generic repository.
- **What must change first:** Amend ADR-002.

### PL-18 — Hangfire

- **Blocking decision:** ADR-002, and ADR-006: "There is no Hangfire and no second scheduler." The poll is one `BackgroundService` plus `sp_getapplock` (`src/TransfersCheck.Api/Transfers/TransferPollWorker.cs`).
- **What must change first:** Amend ADR-002 and ADR-006.

### PL-19 — SignalR

- **Blocking decision:** ADR-002, same adoption sentence. Arrival is the server poll plus FCM web push (ADR-003), not a realtime hub.
- **What must change first:** Amend ADR-002. A hub that replaces the poll also amends ADR-003 and ADR-006.

### PL-20 — Key-value configuration bag

- **Blocking decision:** ADR-007: "There is no key-value `AppConfiguration` bag." Typed options live in `src/TransfersCheck.Core/Options.cs`. The proof's `AppConfiguration` entity is not a source to port.
- **What must change first:** Amend ADR-007.

### PL-21 — Plan tables

- **Blocking decision:** ADR-004: "There is no preapproval, no plan table, no usage cap, and no 500-transaction weekly limit." The commercial number is one typed `PriceAmount`.
- **What must change first:** Amend ADR-004. A catalog of packs (PL-02) or a day pass (PL-01) needs this amendment too.

### PL-22 — Preapproval

- **Blocking decision:** ADR-004, the sentence quoted under PL-21. Story 5.3: "There is no usage cap and no preapproval." Access extends only when the webhook writes `AccessUntil`. The browser return leaves `AccessUntil` unchanged. The proof's `Subscription` entity is not a source to port.
- **What must change first:** Amend ADR-004 and story 5.3.

## Coverage

| ID | Item | Bucket |
| --- | --- | --- |
| PL-01 | 24-hour day pass | Do not schedule |
| PL-02 | Prepaid packs | Do not schedule |
| PL-03 | Merchant credit ledger | Do not schedule |
| PL-04 | Store product | Do not schedule |
| PL-05 | Second payment provider | Do not schedule |
| PL-06 | Native apps | Do not schedule |
| PL-07 | 500-transaction weekly cap | Do not schedule |
| PL-08 | LLM support, including System One | Do not schedule |
| PL-09 | Help publisher and FAQ CMS | Do not schedule |
| PL-10 | Semantic onboarding assistant | Do not schedule |
| PL-11 | Coupons, referrals, and cashback | Do not schedule |
| PL-12 | Auth0 (commerce) | In scope — ADR-018; owner microsite still out |
| PL-13 | Business-config admin UI | Do not schedule |
| PL-14 | Brand manual | Do not schedule |
| PL-15 | Skills and process gates | Do not schedule |
| PL-16 | MediatR | Do not schedule |
| PL-17 | Generic repository | Do not schedule |
| PL-18 | Hangfire | Do not schedule |
| PL-19 | SignalR | Do not schedule |
| PL-20 | Key-value configuration bag | Do not schedule |
| PL-21 | Plan tables | Do not schedule |
| PL-22 | Preapproval | Do not schedule |

Checklist and video: in the MVP, not in the table. `parking-lot.md` "Onboarding" says they are not items to pull from that list.

## Wave close ritual

No wave is authorized to run this. The shape, for a wave added after an amendment, is identical every time:

1. **Unit tests.** `dotnet test tests/TransfersCheck.Tests/TransfersCheck.Tests.csproj`. The project is `tests/TransfersCheck.Tests`. New behavior from that wave has tests here. Existing epic tests still pass.
2. **Swagger.** Desktop browser at the API's `/swagger`. The wave's new or changed routes appear in the TransfersCheck v1 document. Routes listed under "What already exists" still appear.
3. **Browser.** Desktop only, on the `web/` origin. Walk the user flow named in that wave, from sign-in through the new screen or the changed screen, and back to the list with Volver where the screen is not the list.
4. **Validate.** Compare the result with that wave's acceptance checks and with the ADR text after the amendment.
5. **Fix.** Repair failures from steps 1–4 before the close continues.
6. **Commit.** Only when the human asks, during that implementation.
7. **Push.** Only when the human asks, during that implementation.

## Risks

- Implementing any PL item on the current ADRs would contradict the lock dated 21 October 2026, and the 22 October amendments where those records name one.
- Porting `Coupon`, `Subscription`, or `AppConfiguration` from SimpleCheckMercadoPago would build PL-11, PL-22, or PL-20 in the wrong repo.
- The dirty tree already contains the counter shell (menu, wordmark, Configurar usuarios, Salir, Volver). Treating that shell as PL-13 or PL-14 would mix an accepted MVP amendment with parked work.
- `party-viable-product.md` records MVP gaps (checklist "shows done", the first paint spending the refresh cooldown, an unfiltered payments page, real web-push subscription). Those are not parking-lot items. This file does not schedule them and does not close them.
- `decisions.md` "Debt" still blocks a real merchant: Mercado Pago platform credentials, Checkout Pro token, Firebase service-account JSON, and a Cloudflare deploy. Those are operational debts, not waves.

## Assumptions to confirm before any implementation

1. Zero waves is the intended result. This file will not grow a Wave 1 until the human amends the blocking record and asks for a revision.
2. The target repo stays `D:\Repo\transferscheck.com\transferscheck.com`. SimpleCheckMercadoPago stays the proof.
3. System One has no definition in this pack. It stays inside PL-08 until the human defines it.
4. The dirty tree on `mvp/transferscheck` (including `_bmad-output/transferscheck/decisions.md`, `epics-and-stories.md`, `web/src/App.tsx`, and `party-viable-product.md`) is the baseline a later `bmad-build` must re-check. This plan does not commit it. `bmad-build` step 1 stops on a dirty tree before implementation; that gate still applies when a wave exists.
5. `decisions.md` recorded only .NET SDK 9 at preflight, while ADR-001 requires `net10.0`. This pass did not re-check the installed SDK. A future wave cannot build until that toolchain fact is current.
6. Configurar usuarios and the one mark stay the 22 October counter-shell decision. They are not PL-13 or PL-14.
