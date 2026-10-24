# SaaS reconciliation

Superseded 22 October 2026 by the parking-lot review. See `decisions.md`, `adrs.md`, and `open-decisions.md`. Do not treat the commercial locks in this file as current.

**Further supersession 24 Oct 2026:** the Auth0 row below ("Out / Leave parked") is withdrawn for **commerce** identity. Current law is ADR-018. Owner microsite and Punto de Alerta stay out of Auth0. This file remains historical reconciliation, not the live auth model.

Status: proposal for human review. Written 22 October 2026. No application code, no ADR edit, no commit, no push.

This file answers the zero-wave result in `parking-lot-waves.md`. That result still stands for the parking lot. It was not a refusal to finish the product. Every named parking-lot item would replace a locked rule. The sellable product is the locked MVP in `brief.md` "Done when": a person at the counter sees the last 10 incoming transfers, refreshes while a customer waits, and hears an arrived payment; the owner connects Mercado Pago, gets 14 days, then pays ARS 5999 to extend access.

Baseline: branch `mvp/transferscheck`, commit `155f648d2185c289a81b7fc94d169d69520880b5`. The working tree is still dirty. Behavior below is the working tree unless a row says the commit does not contain it. `D:\Compartida\SimpleCheckMercadoPago` was read for comparison. It is not the build target. `decisions.md` "Legacy call shape used by the mock" says the mock follows calls that already worked in the proof and does not copy that application's SaaS shape.

## 1. Recommendation

Accept the locked MVP as the finished SaaS and do not amend `brief.md`, `adrs.md`, `epics-and-stories.md`, or `parking-lot.md`. transferscheck.com already has signup, five logins, server-side Mercado Pago connect, a Spanish checklist, a 14-day trial, one price, Checkout Pro, a poll, an installable PWA, and an FCM sender. Four code gaps still sit between that lock and a merchant who can rely on it: the payment search can drop a transfer of the two required types, opening the list spends the refresh cooldown, the browser stores a mock push token whenever Firebase web keys are present, and a live Checkout Pro notification is refused unless it sends `X-Webhook-Secret`. Close those gaps in three build waves. Leave the parking lot parked. SimpleCheck's plans, coupons, referrals, cashback, Auth0, client-advanced wizard, SignalR, and `AppConfiguration` bag are the product the lock removed; they are not unfinished TransfersCheck.

## 2. Comparison

| Capability | Locked MVP | transferscheck.com code | SimpleCheck code | Proposal |
| --- | --- | --- | --- | --- |
| Sign up and sign in | Email and password. HttpOnly Secure `SameSite=None` cookie. `TenantId` from the server session. `decisions.md` "Party — unnamed contract details". | `POST /api/accounts`, `POST /api/sessions`, `GET /api/session`, `DELETE /api/sessions`. Screens `Entrá` and `Creá tu cuenta` in `web/src/App.tsx`. | `POST /api/auth/register` and `POST /api/auth/login` (local JWT in `localStorage`) plus Auth0 on the same login page. `GET /api/auth/me` provisions from the Auth0 `sub`. | Ship as-is |
| Five logins, one session, revoke, Salir | Up to five accounts. One live session. Owner revoke ends the session and deletes the FCM token. Salir ends the session. ADR-008 and stories 2.2–2.4. | `GET/POST /api/logins`, `DELETE /api/logins/{id}`, `PUT /api/logins/{id}/push`. Working tree: Configurar usuarios, Avisos on the owner row, Revocar hidden on the owner, Salir. Uncommitted `SignOut` clears `LiveSessionId`. That sign-out is not in commit `155f648`. | One user per tenant. No five-login screen. Settings has a browser-local admin password (`Admin` / `Admin123` in `Settings.tsx`). | Ship as-is, including the uncommitted sign-out |
| Counter shell | Header wordmark and one mark. Menu: lista, checklist, configurar usuarios, pagar, Salir. Volver back to the list. `decisions.md` "Human decision — counter shell, 22 October 2026" and the epics amendment. | Working tree `web/src/App.tsx` and `styles.css` (about 389 lines added in `App.tsx`, not in `155f648`): Menú, Lista, Conectar Mercado Pago, Configurar usuarios, Extender acceso, Salir, Volver, inline mark. The pay label is "Extender acceso", not the word "pagar". The approved chip says "Acreditado"; `StatusWords` and the push title say "Llegó". | Navbar across Dashboard, Settings, Payments, Legal, Backend Config. | Ship as-is. Not PL-13 and not PL-14 |
| Connect Mercado Pago | Authorization code plus PKCE. `state` and the verifier in a table with an expiry. Tokens encrypted. One Mercado Pago account, one merchant. The merchant never pastes a secret. ADR-004, ADR-005, stories 3.1, 3.2, 3.5. | `POST /api/connections/mercadopago`, `GET /api/connections/mercadopago/callback`. Trial starts in the callback when `TrialEndsAt` is null. Real client id and secret are still absent (`decisions.md` "Debt"). | `GET /api/mp/auth-url`, `POST /api/mp/connect`, `POST /api/mp/disconnect`, `GET /api/mp/account`, anonymous `GET /api/mp/callback`. PKCE verifier sits in memory for 10 minutes (`MercadoPagoService.GetAuthUrl`). Tokens are stored on the user row in the clear. Wizard step 4 does not call this OAuth; the button advances the step and can be skipped. | Ship as-is. Real credentials stay operational debt, not a product wave |
| Setup checklist | Server step from account, connection, and push token. Client does not advance it. Spanish voseo, optional video URL, alerts and the iPhone line on that checklist. Stories 3.3, 3.4, 3.6. | `GET /api/checklist` returns the brief's strings, done flags, `listReachable`, and `videoUrl`. Working tree `/conectar` paints a done class when connect is done. The alerts button and the iPhone sentence render on the list, not on checklist step 3. `iosHint` is returned and not rendered there. Signup and sign-in navigate to `/`, not to the checklist. | `PUT /api/auth/wizard-step` stores whatever step number the client sends. Wizard screens: name, billing address and tax id, plan cards (trial "$0" / 14 days, "$19", "$49"), then a Mercado Pago button that does not start OAuth. | Build the gap |
| Incoming transfers | Last 10 of `bank_transfer` and `account_money`, on a 5-minute clock. Story 4.1. ADR-006. | `GET /api/transfers`. `TransferPollWorker` calls the sync with `manual: false`. `sp_getapplock` is in `SqlPollLock`. Search is `limit={pageSize}` with no type parameter (`MercadoPagoTransferSource`); `TransferSync` keeps the two types after that page returns. | `GET /api/transactions` (default limit 50). Dashboard asks for 10. `POST /api/transactions/sync`. `TransactionPollingService` polls every 5 minutes and pushes SignalR. `FetchTransactionsAsync` requests 50 rows and then drops other types, same window problem. `POST /api/webhook` inserts one payment and notifies SignalR. | Build the gap |
| Refresh while a customer waits | Same read as the clock. 60-second cooldown. On 429 or 2001, wait until the next round. Story 4.2. | `POST /api/transfers/refresh` with `manual: true`. The list's open effect posts that route (`firstOpen` in `web/src/App.tsx`), so the first paint spends the cooldown. | Dashboard refresh calls `POST /api/transactions/sync`. No 60-second cooldown was seen. | Build the gap |
| Trial and one price | 14 days from connect. Then ARS 5999 every 30 days, one number, from configuration. No usage cap. ADR-004, stories 5.1 and 5.2. | `CommercialOptions`: `TrialDays` 14, `PriceAmount` 5999, ARS, `PaidPeriodDays` 30. `GET /api/billing`. Pay screen shows that amount and "cada {days} días". Day-15 stop is `Entitlement.AllowsSyncAndPush` inside `TransferSync`. | Wizard copy says 14 days. `POST /api/subscriptions/start-trial` writes 7 days and is not called by the wizard. Prices in `CreateSubscription` are hardcoded 999 or 9990, with a comment that they should eventually come from `AppConfiguration`. | Ship as-is |
| Pay and extend access | Checkout Pro on transferscheck's account. The webhook writes `AccessUntil`. The browser return does not. No preapproval and no plan table. Story 5.3. | `POST /api/billing/checkout`, `POST /api/billing/webhook`, `GET /api/billing/return` (redirect only). Webhook returns 401 unless `X-Webhook-Secret` matches. That header is a choice in `decisions.md`, not a sentence in ADR-004. `Checkout:AccessToken` is still absent. | `POST /api/subscriptions/create` builds a preapproval preference with the merchant's Mercado Pago token. `POST /api/webhooks/mercadopago` looks up `subscription_preapproval` and does not update the row (the status write is commented out). Payments shows plan, period, and a "Ver Planes" button with no handler. | Build the gap. Do not amend ADR-004 |
| Installable PWA | One React PWA on Cloudflare Workers. API image does not serve it. Service worker on the UI origin. Android via Chrome. iPhone push after Add to Home Screen. ADR-002, ADR-003, story 6.1. | `web/` Vite app, `manifest.webmanifest`, `public/sw.js`, `wrangler.toml` (`directory = "./dist"`). `Dockerfile` publishes `TransfersCheck.Api` only, on `aspnet:10.0`. Workers deploy was not run (`decisions.md` "Debt"). Icons are the install files already noted in `party-viable-product.md`. | No web app manifest was found under `simple-check-client`. `firebase-messaging-sw.js` is the push worker. The API hosts the client in `wwwroot` in this proof tree. | Ship as-is. Cloudflare account stays operational debt |
| Push when a transfer arrives | FCM HTTP v1 after a new arrived transfer. Foreground sound and notice. Background notification. Tap opens that row first. Only approved accounts. ADR-003, stories 6.2–6.5. | `GET /api/push/config`, `POST /api/push/tokens`. `FirebasePushSender` posts FCM HTTP v1 when `MessagingUrl` is set and reads the service-account file from outside the repo. `sw.js` shows a notification and opens `/?first=`. `enableAlerts` stores `mock-<uuid>` only when `apiKey` is empty; when `apiKey` is set it returns without posting a token. Nothing calls a push subscription. The service-account JSON is still absent. | Settings calls `enablePushNotifications` (`firebase/messaging` `getToken`) when web keys exist. Dashboard subscribes to foreground messages and also to SignalR. | Build the gap |
| Plans, coupons, referrals, cashback | Out. One price. "There is no preapproval, no plan table" (ADR-004). Parking lot: coupons, referrals, and cashback. | Not present. | `Coupon` and `Subscription` are entities and `DbSet`s. `POST /api/subscriptions/apply-coupon` and `create` use them. No client call to `/api/subscriptions` was found. Payments displays `referralCode` and `cashbackAccumulated` from `GET /api/profile`. The only `CashbackAccumulated` write found is the migration column default. | Leave parked |
| Auth0 | Out. ADR-002 and ADR-008. Local JWT and the cookie. | Not present. | Auth0 provider in `main.tsx`, login and register buttons, and `GetMe` keyed by Auth0 `sub`. | Leave parked |
| Client wizard and billing profile | Out. The checklist is server-owned. Pay is not a setup step. No billing-address step. | Not present. | Wizard, `PUT /api/profile/billing`, Payments billing form. | Leave parked |
| SignalR, generic repository, key-value bag | Out. ADR-002 and ADR-007. | `IMerchantStore` / `EfMerchantStore`. Typed options in `Options.cs`. No hub. | `TransactionHub` at `/hubs/transactions`. `EfRepository<T>`. `AppConfiguration` table exists; no controller reads it. | Leave parked |
| Legal page and email test | Not in the MVP. | Not present. | `/legal`. `/backend-config` posts `/api/sandbox/email`. | Leave parked |

Endpoints seen on TransfersCheck and not invented: `POST /api/accounts`, `POST /api/sessions`, `DELETE /api/sessions`, `GET /api/session`, `GET /api/logins`, `POST /api/logins`, `DELETE /api/logins/{id}`, `PUT /api/logins/{id}/push`, `POST /api/connections/mercadopago`, `GET /api/connections/mercadopago/callback`, `GET /api/checklist`, `GET /api/billing`, `POST /api/billing/checkout`, `POST /api/billing/webhook`, `GET /api/billing/return`, `GET /api/transfers`, `POST /api/transfers/refresh`, `GET /api/push/config`, `POST /api/push/tokens`.

Screens seen on TransfersCheck: enter, create account, `/conectar`, `/` (last 10), `/usuarios`, `/pagar`.

Screens seen on SimpleCheck: `/login`, `/register`, `/wizard`, `/dashboard`, `/settings`, `/oauth/callback`, `/backend-config`, `/payments`, `/legal`.

## 3. Amendment list

No locked sentence changes.

`brief.md`, `adrs.md`, `epics-and-stories.md`, and `parking-lot.md` stay as they are. The four gaps are unfinished behavior of rules that are already written. ADR-004 already says the webhook extends `AccessUntil` and the browser return does not. It does not name `X-Webhook-Secret`. That header is a recorded choice in `decisions.md` "Party — unnamed contract details": "The shared secret travels in the `X-Webhook-Secret` header. The locked docs require a match and do not name the header." Wave W3 may replace that choice. Replacing it is not an amendment of ADR-004. If this recommendation is rejected, the replacement does not happen.

## 4. Waves

Three waves. One `bmad-build` invocation each, in `D:\Repo\transferscheck.com\transferscheck.com`. Do not start the next until the close ritual of the current one has passed. Do not invoke `bmad-build` on `parking-lot-waves.md`. Do not build in SimpleCheckMercadoPago.

`bmad-build` step 1 stops on a dirty tree. The human settles the current dirty tree before Wave W1 (see Assumptions). Commit and push inside a wave happen only when the human asks, during that implementation.

The close ritual is the same for every wave:

1. Unit tests: `dotnet test tests/TransfersCheck.Tests/TransfersCheck.Tests.csproj`. New behavior from that wave has tests here. Existing epic tests still pass.
2. Swagger: desktop browser at the API `/swagger`. Routes from section 2 still appear. These waves add no route.
3. Browser: desktop only, on the `web/` origin. Walk the flow named in the wave.
4. Validate against that wave's acceptance and against the current ADR text.
5. Fix failures from steps 1–4 before the close continues.
6. Commit only when the human asks.
7. Push only when the human asks.

### W1 — The last 10 are the two transfer types, and opening the list does not press the button

Goal: a live account shows the last 10 incoming `bank_transfer` and `account_money` rows, and the first paint of the list leaves the 60-second button ready.

Why this order: the counter exists to see the transfer that just arrived. The mock seed hides both bugs. Push and payment are useless if the row is missing or the button is already cooling down.

Touch surfaces: `MercadoPagoTransferSource.ReadIncomingAsync`, `TransferSync` if the read has to ask for another page to fill 10 rows of those two types, the list open effect in `web/src/App.tsx` that posts `/api/transfers/refresh`, and `tests/TransfersCheck.Tests` (Epic 4). No new endpoint. The worker stays `manual: false`. The button stays `manual: true`.

Acceptance:

- Story 4.1: the stored list is the last 10 of the two seeded types, including when newer payments of other types occupy the first page.
- Story 4.2 and ADR-006: opening the list does not set `RefreshNotBefore`. A later tap inside 60 seconds is the cooldown. HTTP 429 or error 2001 still waits until the next round.
- The button label and the wait sentence may stay as the working tree has them. Restoring "Actualizar" and "Esperá un momento." is not this wave.

Browser flow: sign in, open the list, confirm Actualizar is usable without waiting from the open itself, then trigger one manual refresh and see the cooldown. A second case, against the mock or a fixture, has other payment types newer than a `bank_transfer` and the list still shows that transfer.

### W2 — The checklist is where connect and alerts finish, including a real push token when keys exist

Goal: after signup the merchant sees the checklist until Mercado Pago is connected; alerts permission, the iPhone line, and token registration happen on that checklist; a non-empty Firebase web config stores a real registration token.

Why this order: W1 makes the list worth opening. This wave is the path that connects the account and arms the phone. The API sender and `sw.js` already exist, so the wave does not add a second push stack.

Touch surfaces: `web/src/App.tsx` (where signup and sign-in land, checklist step 3, `enableAlerts`), `web/public/sw.js` only if a background notification does not reach the current `push` listener, and Epic 3 / Epic 6 tests. `GET /api/push/config` and `POST /api/push/tokens` stay. `FirebasePushSender` stays the sender. No Auth0, no Firebase MCP, no SignalR.

Acceptance:

- Stories 3.3 and 3.4: the checklist is the screen after signup while `connectDone` is false. The connect step shows done once a `MerchantConnection` exists, and the list is reachable then. The client has no call that advances the step.
- Activate alerts, including the server's `iosHint`, is on that checklist. The step shows done when a push token exists. The trial line stays status. Pay stays off the checklist.
- Story 6.2: empty web keys still store a `mock-` token, as `decisions.md` already allows. Non-empty keys store a token from the browser push registration and post it. The service-account JSON stays out of the repo and out of the client.
- Stories 6.3 and 6.4: an open tab still beeps and shows the on-page notice for `approved` only. A background notification shows amount, payer, and the arrived word, and a tap opens `/?first=` for that row. Pending and rejected stay silent.
- Story 6.5: Avisos on Configurar usuarios, including the owner row, is unchanged. This wave does not move those controls back onto the list.

Browser flow: create an account, land on the checklist, connect against the mock, see the connect step done, activate alerts, return to the list. With empty keys, the mock token path is enough. A real locked-screen notification waits on the Firebase files in Assumptions; the wave's automated proof is the token post and the service worker's click URL.

Uncertainty: this pass did not run a browser and did not send a live FCM message. `enableAlerts` was read. It does not subscribe. Whether `sw.js` receives an FCM payload without the Firebase messaging SDK is unproven; the wave proves it and edits the worker only if that proof fails.

### W3 — A real Checkout Pro notification extends access; the browser return does not

Goal: an approved payment on transferscheck's Mercado Pago account extends `AccessUntil` by 30 days, once; the success URL does not.

Why this order: the trial starts at connect, so the list and the phone can be proven before money. Selling past day 14 needs this wave. It is last because it depends on a human confirmation (Assumptions) and on `Checkout:AccessToken`, which is still absent.

Touch surfaces: `BillingEndpoints.Webhook` and Epic 5 tests. `GET /api/billing/return` stays a redirect. No plan table, no preapproval, no coupon, no second price.

Acceptance, story 5.3 and ADR-004:

- The webhook extends `AccessUntil` from the current valid end, or from payment time if access has expired.
- A replay of the same payment id does not extend again.
- The browser return leaves `AccessUntil` unchanged.
- The check that the caller is Mercado Pago must succeed for a notification Checkout Pro actually sends. The current `X-Webhook-Secret` header is the choice being replaced, only after the human confirms. This wave does not invent a second commercial clock.

Browser flow: from `/pagar`, the screen still shows one number from `GET /api/billing` and one pay button. The return URL comes back to the list without a longer `AccessUntil` unless the webhook ran. The live charge itself waits on `Checkout:AccessToken`.

### Not waves

These block a real merchant and are already `decisions.md` "Debt". They are not `bmad-build` waves:

- Mercado Pago platform client id, client secret, redirect URI, and one real OAuth (operator checklist step 7 in `mp-authorization-setup.md`).
- `Checkout:AccessToken` on transferscheck's account.
- Firebase service-account JSON supplied to the API by path, and public web keys if W2 is to store a real token.
- A Cloudflare account. `wrangler.toml` is present. The Workers deploy was not run.
- Rotate the proof secrets named in `decisions.md` "Secret rotation". Do not copy them.
- A container connection string that is not Windows integrated authentication, and no `TrustServerCertificate=True` in production.

## 5. Still parked

Not required to finish the SaaS the lock already defines. Scheduling any of them still requires the amendment named in `parking-lot-waves.md`. This proposal does not make those amendments.

| IDs | Why they stay parked |
| --- | --- |
| PL-01, PL-02, PL-07, PL-21 | A day pass, packs, a weekly cap, and plan tables replace "one price, ARS 5999 every 30 days" and "no usage cap" (ADR-004, `brief.md` "Commercial terms"). |
| PL-03, PL-11 | A ledger, coupons, referrals, and cashback are a second price or a balance. SimpleCheck shows the fields. `CashbackAccumulated` is not updated by the controllers that were read. The locked clock is `AccessUntil`. |
| PL-22 | Preapproval is a second billing product. SimpleCheck's preapproval webhook does not even write the status. Checkout Pro is the locked charge. |
| PL-04 | A store, a role matrix, and a per-seat price are a different product (ADR-008). Five logins are already in the MVP. |
| PL-05 | A second transfer source stays an empty port (ADR-005). |
| PL-06 | A native app is a second client (ADR-003). The PWA is the install. |
| PL-08, PL-10 | LLM support and the semantic assistant were rejected. The checklist is not a chat (ADR-004). System One is still undefined. |
| PL-09, PL-14, PL-15 | A help CMS, a brand manual, and extra process gates are not how a merchant gets paid or notified. The video is one typed URL. The counter-shell mark is already in the working tree. |
| PL-12 | Auth0 replaces the local cookie session (ADR-002, ADR-008). SimpleCheck already has that split and the lock refused it. |
| PL-13, PL-20 | A business-config screen and an `AppConfiguration` bag replace typed settings (ADR-007). SimpleCheck's table is unused by any controller that was read. Configurar usuarios is the five-login screen, not this item. |
| PL-16, PL-17, PL-18, PL-19 | MediatR, a generic repository, Hangfire, and SignalR are architecture exclusions (ADR-002, ADR-006). The poll and FCM are the arrival path. SimpleCheck's hub is the shape that was refused. |

The legal page and the SMTP test form are also out. They are not in `parking-lot.md` and they are not needed for the locked "Done when".

## 6. Assumptions the human must confirm

1. The locked MVP is the finished SaaS. Zero amendments in section 3 is the intended result. Rejecting that means naming which SimpleCheck behavior (plans, coupons, referrals, Auth0, the wizard, SignalR) should come back, and amending the ADR first. This file will not grow a parking-lot wave on its own.
2. Wave W3 may replace the `X-Webhook-Secret` choice in `decisions.md` so a Checkout Pro notification can extend `AccessUntil`. ADR-004's outcome stays. If this is rejected, W3 does not run and a real payment stays blocked.
3. The working tree on `mvp/transferscheck` at `155f648` is still dirty. Modified: `_bmad-output/transferscheck/decisions.md`, `_bmad-output/transferscheck/epics-and-stories.md`, `src/TransfersCheck.Api/Accounts/AccountEndpoints.cs`, `tests/TransfersCheck.Tests/Epic2LoginTests.cs`, `web/e2e/onboarding.spec.ts`, `web/index.html`, `web/public/manifest.webmanifest`, `web/src/App.tsx`, `web/src/styles.css`. Untracked here: `parking-lot-waves.md`, `party-viable-product.md`, `web/public/icon.svg`, `web/tsconfig.tsbuildinfo`. This proposal does not commit that tree. The counter shell and the server-side Salir live in those uncommitted edits. Dropping them makes stories 2.3 and the 22 October shell look missing again.
4. Working-tree copy may stay: menu "Extender acceso", chip "Acreditado", button "Actualizar ahora". The recorded words are "pagar", "Llegó", and "Actualizar" (`decisions.md`). Confirm that drift or ask a later pass to restore the words. It is not one of the three waves.
5. `D:\Compartida\SimpleCheckMercadoPago` is the proof to compare. No wave lands there. Do not copy `Coupon`, `Subscription`, or `AppConfiguration`. Do not reuse its secrets.
6. Operational debt in section 4 stays outside the waves. A merchant on a real phone still needs credentials, the Firebase files, and a Workers deploy after the three waves.
7. `decisions.md` "Already done" recorded only .NET SDK 9 at preflight. ADR-001 and the Dockerfile require .NET 10. This pass did not recheck the installed SDK.
8. This pass did not start the apps and did not click through a browser. Rows that say "wired" are from the controllers and pages that were opened. Where a handler is missing, the row says so.
