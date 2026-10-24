# Product Brief: transferscheck.com

Locked 21 October 2026. Amended 22 October 2026: the UI and the API are two deployables. Amended 22 October 2026 during the parking-lot review. Amended 23 October 2026 from human decisions that day, including the night close of the weekly-cap cutoff and the adopted UI labels. A reviewer accepts or rejects this pack. The decisions below are closed. What is still unknown is `later-research.md`, indexed from `open-decisions.md`.

Companion files in this folder: `epics-and-stories.md`, `adrs.md`, `parking-lot.md`, `mp-authorization-setup.md`.

## Executive Summary

transferscheck.com, on a schedule and on demand, shows the last 10 incoming Mercado Pago transfers (`bank_transfer` and `account_money`). The commerce can see that the money arrived without opening Mercado Pago and without the admin's Mercado Pago password.

The primary client is the webapp in the commerce browser, on the computer where stock and collections already run. The phone is the Pack Evento situation. There is no native app and no app-store client. The client is one React webapp, installable, deployed on Cloudflare Workers. The API is a separate deployable and does not host it. The two are one product: the UI calls the API over an explicit contract, across origins.

The pay screen shows the active plans. The two starting offers are Pack Evento and Pack Mes. Pack Mes has a 21-day trial. The clock starts when Mercado Pago is activated and the account is linked. Access during the trial ends 21 days after that link, at that same clock time. It does not end sometime in day 22. While the webapp is open, a new transfer appears immediately. The poll and the browser notification stay.

## The Problem

Someone has to confirm that an incoming transfer arrived. Doing that in Mercado Pago puts the admin's password on the device, or waits until the admin is there. The screen needs Fecha, Pagador, Monto, and Estado. On Pack Evento that screen is a phone. Otherwise it is the commerce computer.

## The Solution

The server reads the merchant's incoming Mercado Pago transfers and keeps the last 10 of the two seeded types. A clock runs the read. A button runs the same read when the customer is standing there. The list columns are Fecha, Pagador, Monto, and Estado. An empty payer name is Desconocido. Amounts use es-AR and ARS. Dates use `dd/MM/yyyy HH:mm`. The admin history title is Historial de Transacciones. That history is not capped at 10 rows. The alert point does not get that title. An arrived transfer plays a sound and raises a notice. One tap opens the list with that row first.

Signup creates the commerce. One first-run walk then runs in the product UI, in Spanish (voseo), before the transfer list is useful. Activating Mercado Pago and linking the account starts the Pack Mes trial. Paying extends access by the plan's period. On the access date the web blocks until they pay, and new transfers and alerts stop. Pay is not a setup step. Everything after the first run lives on the help page. Pack Mes help can take a free question. Pack Evento help is a static menu.

## What Makes This Different

The commerce sees the arrival on transferscheck. The admin's Mercado Pago password stays off the other person's device. The pay screen shows the active plans.

## Who This Serves

The commerce admin, on the computer where stock and collections already run. Pack Evento is that admin alone, including on a phone. Pack Mes adds up to four Punto de Alerta accounts. The second role is Punto de Alerta. This pack does not say cashier and does not talk about tills.

This is one commerce with a shared list. It has no permission matrix and no per-seat price. The commerce admin has dominion over their own tenant account, including transaction history, a CSV report, and the grand total of transfers by day, by month, and by a date range. A Punto de Alerta sees the last N rows, default 10, and may open the detail of one of those rows, because Mercado Pago sometimes does not show the sender name. They receive alerts. They cannot open history, a CSV, the totals, or Mercado Pago account settings. They do not manage accounts.

The product owner is a superadmin on a separate microsite. That person is not a commerce.

## Scope

In this MVP:

- Last 10 incoming transfers, types `bank_transfer` and `account_money`, on a clock and from a button.
- Pack Mes: one commerce admin and up to four Punto de Alerta accounts. Pack Evento: the admin only. One live session each. The admin can deactivate a Punto de Alerta.
- Mercado Pago connect (authorization code + PKCE, server-side) and encrypted tokens. One platform application. The commerce grants OAuth consent and does not create an application, paste a client secret, or upload a JSON.
- One server-owned first-run walk after signup. The client does not advance the step. Later guidance is the help page. No chatbot on the connect screen.
- Pack Mes: 21 days from the moment Mercado Pago is activated and the account is linked, then ARS 5999 for 30 days. Access during the trial ends at that same clock time 21 days later. Pack Evento: ARS 1000 for 24 hours. The commerce pays by a Mercado Pago payment link into the owner's account.
- Installable React webapp and Firebase Cloud Messaging web push for arrived transfers. While the webapp is open, a new transfer also appears immediately. The poll stays.
- Help pages in `web/help/`. A plan table with `EnableCopilotHelp`. Pack Mes help is a copilot chatbot. Pack Evento help is a static menu. A referral code. A weekly cap of 500 queries, with a warning when 50 remain. The owner microsite for plans and the services panel. The five-day notice and the referral bonus on Hangfire. The poll stays on `TransferPollWorker`.

Out of this MVP: `parking-lot.md`. What is still unknown: `later-research.md`, indexed from `open-decisions.md`. The first bmad-build is the note in that file. The owner microsite, the help copilot, Jev, and the production pipeline stay decided and are not that build.

## UI rules

The list columns are Fecha, Pagador, Monto, and Estado. There is no Medio column and no search box. There is no Sincronizar control. The menu is Transacciones, Configurar, Pagos, and Salir, with Cerrar sesión in the menu. The login door copy is story 2.1. The pay screen labels are Email and Plan. The referral label is Mi código. There is no cashback balance. The notification button is Habilitar Notificaciones. The one legal sentence is in ADR-004. Each screen has one primary verb. The pay screen shows the active plans, including the Pack Evento face copy. Refresh and the amount stay easy to hit, including on a phone during Pack Evento. Screens: enter, create account, the first-run walk, last 10, pay, and the help page. Alert permission and add-to-home on iOS stay on the first-run walk. That walk is not a chat.

`Docs/ManualdeMarcayUI.html` is a reference while designing, for visual, technical, and wording cohesion. It is not a product module and not an approval gate. Sentences in it that still say a 14-day trial, day 15, or one price are wrong after this review. The file was not rewritten. The list of those sentences is in `decisions.md`.

## Setup checklist

After signup, and before the transfer list is useful, the commerce admin sees one first-run walk. It runs once. The UI is Spanish, voseo. The server stores the current step per tenant and sets it from real state: the account exists, a `MerchantConnection` exists, a push token exists. The client renders that step. It does not advance the step by itself. Later questions live on the help page.

Steps, in order:

1. Create the account. This is already in the MVP. Once the account exists, the step shows done.
2. Connect Mercado Pago. One primary button. Short numbered help beside it: you must manage that Mercado Pago account; tap Connect; sign in and accept; you return here. A video embed or link sits on this step. The video URL is typed configuration, `Onboarding:MercadoPagoVideoUrl`. An empty URL hides the player and leaves the steps. Skipping the video does not block the button. The merchant video shows only this consent path. It does not show how to create a Mercado Pago application or a client secret. That recording is `mp-authorization-setup.md`, and it is not shown to a Punto de Alerta.
3. When the connection is stored, this step shows done and the last-10 list is reachable.
4. Activate alerts. Notification permission, and on iOS Add to Home Screen. Already in the MVP. Same checklist, next step. The server marks it done when a push token exists for the tenant.
5. Trial is running. That line is status on Pack Mes. The trial still starts when Mercado Pago is activated and the account is linked, so the line can be true while alerts are the open step. Pay is not a setup step. This line does not say 14 days. Access ends 21 days after that link, not sometime in day 22.

Example copy:

- Account: `Creá tu cuenta`
- Button: `Conectar Mercado Pago`
- Reconnect: `Re-conectar cuenta`. Confirm: `¿Estás seguro que deseas re-conectar tu cuenta de Mercado Pago?`
- Connection badge: `Conectado` / `No conectado`. These are not the live-channel badge.
- Help: `1. Tenés que administrar esa cuenta de Mercado Pago. 2. Tocá Conectar. 3. Iniciá sesión y aceptá. 4. Volvés acá.`
- Alerts: `Activá los avisos`
- iOS, on that same step: `En el iPhone, agregá la app a la pantalla de inicio.`
- Trial line: `La prueba está en curso.`

There is one platform Mercado Pago application. Merchants only grant OAuth consent. The one-time operator setup, including the film script, is `mp-authorization-setup.md`.

## Commercial terms

| Term | Locked value |
| --- | --- |
| Pay screen | The active plans. Not one number. |
| Pack Evento | ARS 1000. 24 hours. One user, the commerce admin. Face copy: "Un día, un usuario. Ideal para la fiesta o el show." `EnableCopilotHelp` is off. Help is a static menu. A free question is refused. |
| Pack Mes | ARS 5999. 30 days. One admin and up to four Puntos de Alerta. Trial is 21 days from the Mercado Pago link. `EnableCopilotHelp` is on. Help is a copilot chatbot. The person can ask a question. |
| More plans | A table. The owner adds and edits plans on the microsite. |
| Quantity packs | 50, 100, 500, and 1000+ are discarded. |
| Trial stop | 21 days after Mercado Pago is activated and the account is linked, at that same clock time. Not sometime in day 22. |
| Five days before the date | An email and a web banner. Access still works. Not for Pack Evento. |
| On the date | The web blocks until they pay. New transfers and alerts stop. |
| Payment | A Mercado Pago payment link. Money lands in the SaaS owner's Mercado Pago account, the one set in the services panel. Not the commerce collection account. No preapproval, no automatic debit, no extra debit layer. |
| While days remain | The new period continues without a gap. |
| After the block | The new period starts at payment. |
| Browser return | Does not itself move the date. The owner does not mark the payment by hand. |
| Leftover days | Not refundable as money. No cashback. No discount coupons. |
| Weekly cap | Typed `WeeklyQueryCap` of 500 queries. Warning gap 50, which is 10 percent. The commerce is warned when 50 remain, at 450. At 500, new queries and alerts wait until the next week. Message on the web. No overage charge. Same idea for both current packs. The weekly-cap cutoff instant is the moment Mercado Pago is linked. From that instant, seven consecutive days run. The next period starts when the previous one ends. It is not a calendar week and not Monday. The 21-day trial uses that same start. A later recharge of 500 is not in scope. |
| Referral | A code only. The referrer's next Pack Mes is free, once, and only while that referrer is inside an active plan. |

Prices live on the plan table so the pay screen does not hardcode one number.

## Behavior the reviewer is accepting

- The UI deploys on Cloudflare Workers. The API is its own deployable and does not serve the PWA. Browser calls are cross-origin: the API allow-lists the UI origin, and the session cookie travels on credentialed requests.
- Android install is Chrome. On iPhone, push works after Add to Home Screen. The service worker belongs to the UI origin.
- Production push is FCM HTTP v1 from the API, sent after a new transfer is stored. The Firebase service-account JSON stays out of the repo, out of the client, and out of commits.
- Foreground: a short sound and an on-page notice. Background or locked screen: a system notification with sound. The card shows amount, payer name, and the word for arrived. Pending and rejected stay on the list and stay silent.
- The commerce admin can deactivate a Punto de Alerta from the server. That session ends, its FCM token is deleted, and polling for that browser stops.
- A new sign-in on the same account closes the previous session. `TenantId` comes from the server session. The session cookie is HttpOnly, Secure, and short-lived. Because the UI and the API are different origins, that cookie is cross-site (`SameSite=None`) and the UI sends it on credentialed API calls.
- Commerce admin sign-in is email, password, and a code sent to that same email. The alias is chosen once, shown, confirmed, and never changed.
- While the webapp is open, a new transfer appears immediately. The poll and the browser notification stay. No hub method is named.
- Hangfire.AspNetCore and Hangfire.SqlServer 1.8.25 are already on TransfersCheck.Api. The dashboard is only in Development at `/hangfire`. The five-day notice and the referral bonus are Hangfire jobs. No other Hangfire job is registered. The poll stays `TransferPollWorker`.
- Poll default is 5 minutes. Button cooldown default is 60 seconds. On Mercado Pago 429 or 2001, that merchant waits until the next round.
- A transfer is unique on (`TenantId`, `Source`, `ExternalId`). One Mercado Pago account links to only one merchant.
- OAuth `state` and the PKCE verifier sit in a table with an expiry. Client id and client secret that were in the proof source are rotated before any deploy. This pack does not record those values.

## Done when

A commerce admin, or a Punto de Alerta on Pack Mes, sees incoming transfers of the two types, refreshes while a customer waits, and hears an arrived payment without opening Mercado Pago. The alert point's list length defaults to 10. The admin connects Mercado Pago and gets 21 days on Pack Mes from that link, then pays the active plan to extend access. On the access date the web blocks until they pay, and new transfers and alerts stop. Pack Evento is 24 hours for that admin alone.
