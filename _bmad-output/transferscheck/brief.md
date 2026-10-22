# Product Brief: transferscheck.com

Locked 21 October 2026. Amended 22 October 2026: the UI and the API are two deployables. A reviewer accepts or rejects this pack. The decisions below are closed.

Companion files in this folder: `epics-and-stories.md`, `adrs.md`, `parking-lot.md`, `mp-authorization-setup.md`.

## Executive Summary

transferscheck.com, on a schedule and on demand, shows the last 10 incoming Mercado Pago transfers (`bank_transfer` and `account_money`). Whoever is at the counter can see that the money arrived without opening Mercado Pago and without the owner's Mercado Pago password.

The client is one React PWA, installable, deployed on Cloudflare Workers. The API is a separate deployable and does not host the PWA. The two are one product: the UI calls the API over an explicit contract, across origins. The owner pays one price, ARS 5999 every 30 days. A 14-day trial starts when Mercado Pago is connected. Up to five login accounts share one paying merchant. When a transfer arrives, the API sends a web push to the accounts the owner has approved.

## The Problem

The customer is standing at the counter. Someone has to confirm that an incoming transfer arrived. Doing that in Mercado Pago puts the owner's password on the counter device, or waits until the owner is there. The counter needs who paid, the amount, when, and the status.

## The Solution

The server reads the merchant's incoming Mercado Pago transfers and keeps the last 10 of the two seeded types. A clock runs the read. A button runs the same read when the customer is standing there. The list shows who, amount, when, and status. An arrived transfer plays a sound and raises a notice. One tap opens the list with that row first.

Signup creates the merchant. A checklist then runs in the product UI, in Spanish (voseo), before the transfer list is useful. Connecting Mercado Pago starts the trial. Paying extends access. Day 15 with no payment stops sync and push. The last list stays on screen. Pay is not a setup step.

## What Makes This Different

The counter sees the arrival on transferscheck. The owner's Mercado Pago password stays off that device. The pay screen shows one number.

## Who This Serves

The paying merchant. The owner is one of five login accounts and can create the other four. Those accounts are for people at the counter.

This is one merchant with a shared list. It has no role matrix and no per-seat price.

The owner approves which of the five accounts receive push.

## Scope

In this MVP:

- Last 10 incoming transfers, types `bank_transfer` and `account_money`, on a clock and from a button.
- Five login accounts, one live session each, owner revoke.
- Mercado Pago connect (authorization code + PKCE, server-side) and encrypted tokens. One platform application. The merchant grants OAuth consent and does not create an application, paste a client secret, or upload a JSON.
- A server-owned setup checklist after signup. The client does not advance the step.
- 14-day trial from the moment Mercado Pago is connected, then ARS 5999 for 30 days via Checkout Pro.
- Installable React PWA and Firebase Cloud Messaging web push for arrived transfers.

Out of this MVP: `parking-lot.md`.

## UI rules

The list feels like a Mercado Pago payment list: who, amount, when, status. Each screen has one primary verb. The pay screen has one price. Refresh and the amount are large and thumb-reachable. Screens: enter, create account, the setup checklist, last 10, pay. Alert permission and add-to-home on iOS stay on the checklist. The checklist is not a chat.

## Setup checklist

After signup, and before the transfer list is useful, the merchant sees one checklist. The UI is Spanish, voseo. The server stores the current step per tenant and sets it from real state: the account exists, a `MerchantConnection` exists, a push token exists. The client renders that step. It does not advance the step by itself.

Steps, in order:

1. Create the account. This is already in the MVP. Once the account exists, the step shows done.
2. Connect Mercado Pago. One primary button. Short numbered help beside it: you must manage that Mercado Pago account; tap Connect; sign in and accept; you return here. A video embed or link sits on this step. The video URL is typed configuration, `Onboarding:MercadoPagoVideoUrl`. An empty URL hides the player and leaves the steps. Skipping the video does not block the button. The merchant video shows only this consent path. It does not show how to create a Mercado Pago application or a client secret. That recording is `mp-authorization-setup.md`, and it is not shown to cashiers.
3. When the connection is stored, this step shows done and the last-10 list is reachable.
4. Activate alerts. Notification permission, and on iOS Add to Home Screen. Already in the MVP. Same checklist, next step. The server marks it done when a push token exists for the tenant.
5. Trial is running. That line is status. The trial still starts when Mercado Pago is connected, so the line can be true while alerts are the open step. Pay is not a setup step.

Example copy:

- Account: `Creá tu cuenta`
- Button: `Conectar Mercado Pago`
- Help: `1. Tenés que administrar esa cuenta de Mercado Pago. 2. Tocá Conectar. 3. Iniciá sesión y aceptá. 4. Volvés acá.`
- Alerts: `Activá los avisos`
- iOS, on that same step: `En el iPhone, agregá la app a la pantalla de inicio.`
- Trial line: `La prueba está en curso.`

There is one platform Mercado Pago application. Merchants only grant OAuth consent. The one-time operator setup, including the film script, is `mp-authorization-setup.md`.

## Commercial terms

| Term | Locked value |
| --- | --- |
| Price on screen | ARS 5999 every 30 days, one number |
| Config | `PriceAmount` 5999, currency ARS, `PaidPeriodDays` 30, `TrialDays` 14 |
| Trial start | Mercado Pago connected. Signup leaves the trial unstarted. |
| Day 15, unpaid | Sync and push stop. The last list stays visible. |
| Payment | Checkout Pro on transferscheck's own Mercado Pago account, separate from the merchant account being read. |
| What extends access | The webhook writes `AccessUntil`. The browser return leaves `AccessUntil` unchanged. |
| Extension rule | 30 days from the current valid end, or from payment time if access has already expired. |
| Usage | No usage cap. |

The price is configuration so UI logic does not hardcode it. The commercial number is 5999.

## Behavior the reviewer is accepting

- The UI deploys on Cloudflare Workers. The API is its own deployable and does not serve the PWA. Browser calls are cross-origin: the API allow-lists the UI origin, and the session cookie travels on credentialed requests.
- Android install is Chrome. On iPhone, push works after Add to Home Screen. The service worker belongs to the UI origin.
- Production push is FCM HTTP v1 from the API, sent after a new transfer is stored. The Firebase service-account JSON stays out of the repo, out of the client, and out of commits.
- Foreground: a short sound and an on-page notice. Background or locked screen: a system notification with sound. The card shows amount, payer name, and the word for arrived. Pending and rejected stay on the list and stay silent.
- The owner can revoke a login from the server. That session ends, its FCM token is deleted, and polling for that device stops.
- A new login on the same account replaces the previous session. `TenantId` comes from the server session. The session cookie is HttpOnly, Secure, and short-lived. Because the UI and the API are different origins, that cookie is cross-site (`SameSite=None`) and the UI sends it on credentialed API calls.
- Poll default is 5 minutes. Button cooldown default is 60 seconds. On Mercado Pago 429 or 2001, that merchant waits until the next round.
- A transfer is unique on (`TenantId`, `Source`, `ExternalId`). One Mercado Pago account links to only one merchant.
- OAuth `state` and the PKCE verifier sit in a table with an expiry. Client id and client secret that were in the proof source are rotated before any deploy. This pack does not record those values.

## Done when

A person at the counter, signed in with one of the five accounts, sees the last 10 incoming transfers of the two types, refreshes while a customer waits, and hears an arrived payment without opening Mercado Pago. The owner connects Mercado Pago, gets 14 days, then pays ARS 5999 to extend access. Unpaid day 15 leaves the last list up and stops sync and push.
