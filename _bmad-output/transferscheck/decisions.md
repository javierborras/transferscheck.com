# Decisions

Assumptions and non-blocking debt for the TransfersCheck MVP. Locked product behavior stays in `brief.md`, `epics-and-stories.md`, `adrs.md`, `parking-lot.md`, and `mp-authorization-setup.md`. The record dated 22 October 2026, parking-lot review, amends commercial locks in the earlier entries. The record dated 23 October 2026 wins where it conflicts with that review. The record dated 23 October 2026, night, wins where it conflicts with the earlier 23 October entries. Those entries stay as history. What is still unknown is `later-research.md`, indexed from `open-decisions.md`. No secret values are recorded here.

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

- ~~Login is email and password.~~ Superseded 24 Oct 2026 for commerce admin: Auth0 Universal Login (SSO or Database email/password), then API session cookie (ADR-018). Email remains the commerce-admin identifier unique across tenants for tenant resolution. Punto de Alerta stays local usuario/clave. A failed sign-in is one generic refusal where the product owns the form.
- The signup account is the owner (`IsOwner`). That flag is not a role matrix. The owner creates the other accounts and revokes them. The owner account is not revoked by this action.
- `Session:Lifetime` is typed configuration. The default is 12 hours. "Short-lived" in the ADR did not name 12 hours. This default is the choice. The cookie remains HttpOnly, Secure, and `SameSite=None`.
- Authorization query sends `code_challenge`, `code_challenge_method=S256`, and `state`. It does not send `code_method`. It does not send `platform_id`. The mock accepts that set. If a live application requires the other name, that rename is part of the existing real-OAuth debt.
- The second credential is `Checkout:AccessToken`, typed configuration, user-secrets or the environment, never the repo. It is the Checkout Pro credential on transferscheck's account, not a third merchant secret.
- The webhook extends `AccessUntil` only when the configured shared secret matches, the payment id has not been applied before, and the payment status is approved. A replay does not extend access again. The browser return does not change `AccessUntil`.
- The shared secret travels in the `X-Webhook-Secret` header. The locked docs require a match and do not name the header. Checkout Pro calls the same `MercadoPago:ApiBaseUrl` as the read path, with `Checkout:AccessToken`, so the host stays in configuration and the charging account stays separate.
- Stored status stays the source value. The screen and the push card say Llegó for `approved`, Pendiente for `pending`, and Rechazado for `rejected`. Any other status is shown as the raw value and stays silent.
- Push approval and the other four logins are controls on the last-10 screen for the owner. They are not a new menu and not a new story. The screens stay the ones in the brief. Superseded 22 Oct 2026 by "Human decision — counter shell" below. This sentence stays as the previous wording.
- Public Firebase web keys, if they ever exist, are typed configuration separate from the service-account JSON. Empty keys mean the client registers a mock token. The service-account JSON stays out of the repo.
- The local UI origin is `http://localhost:5183`. Port 5173 was already taken by another process on this machine. The origin stays in `Cors:UiOrigin` and `Ui:Origin`.
- The last-10 response includes `pollIntervalSeconds` and `buttonCooldownSeconds` from `Sync` so the PWA does not hardcode those intervals. The stories did not name the Spanish words for enter, refresh, pay, revoke, or the extra-login control. The screen uses Entrá, Correo, Contraseña, Actualizar, Pagar, Revocar, Agregar cuenta, and Avisos. Door words Entrá, Correo, and Contraseña, and the menu words, were superseded 23 Oct 2026, night. Esperá un momento. is the line while the refresh control is waiting. A failed sign-in says El correo o la contraseña no sirven. A failed signup says No se pudo crear la cuenta.

## Visual pass — 22 October 2026

The locked docs do not name colors. The first UI was a dark green field, white inputs, a browser-blue link, and the same quiet type for Llegó, Pendiente, and Rechazado.

Assumption for this pass: a light sheet, so a counter can read payer names and amounts. The amount stays the largest text in the row. The three status words stay the server words and sit in separate chips. The connect screen shows the three step titles the server already returns, and a done step is marked, because the story says that step shows done. No new step, field, price, or copy.

## Party — viable product, 22 October 2026

Proposals only. The table did not close these. They are not locked behavior, and they are not work already done. The debate is `party-viable-product.md`. Menus, a logo, and onboarding beyond the checklist were closed as not required. Those are not repeated here. The menu and the mark in that close are superseded the same day by "Human decision — counter shell" below. The parking lot stays closed.

- **Checklist after connect.** `brief.md` and stories 3.3 and 3.4 say the connect step shows done, and that alerts stay on that checklist. The running client unmounts the checklist when connect is done, so that moment is not on screen. The table did not agree whether a fully finished checklist must stay on the counter. Proposal, not adopted: when a connection is stored, mark the connect step done, keep alerts on that same checklist until a push token exists, and keep the last-10 list reachable. Once alerts are done, the counter screen is the list and the trial line, without a permanent checklist. No new step and no new copy.
- **Return from pay.** Several voices closed this as not required, because the pay screen has one primary verb. Two voices did not. Proposal, not adopted: one text link from `/pagar` to `/`, not a second button and not a menu. The pay button stays the only button.
- **Owner push approval.** `PushApproved` defaults false, and the owner row has no Avisos control, so the owner's phone is not in the send list. The table did not close this. Proposal, not adopted: the owner row uses the same Avisos control as the other accounts. That control still does not revoke the owner. Superseded 22 Oct 2026: the human adopted that control. See "Human decision — counter shell".

## Human decision — counter shell, 22 October 2026

The human rejected the counter screen. Their words: it is horrible, something was lost along the way, there is no menu, no brand identity, no options, no logout (Salir), no back (Volver), and no place to configure users (configurar usuarios). They consider that screen a failure.

This overrides the same-day party close that menus, a logo, and an onboarding tour are not in the epics. The party is not reopened. `parking-lot.md` stays closed: no brand manual, no assistant, no native app, no packs, no help CMS.

Previous wording this replaces, from "Party — unnamed contract details": "Push approval and the other four logins are controls on the last-10 screen for the owner. They are not a new menu and not a new story. The screens stay the ones in the brief."

Menu labels in this paragraph were superseded 23 Oct 2026, night. The counter screen has a header with the word TransfersCheck and one mark, and a menu: lista, checklist (Conectar Mercado Pago), configurar usuarios, pagar, and Salir. Volver on every screen that is not the list returns to the list. Configurar usuarios is its own screen for this tenant's login accounts: agregar cuenta, revocar, and avisos. The owner row has avisos, and that control does not revoke the owner. `PushApproved` is settable for the owner. The five-account cap stays. Salir ends the live session and returns to the login screen. Login and signup show the same wordmark and do not show the authenticated menu.

## Human decision — parking-lot review, 22 October 2026

Source: the human, during the parking-lot review. This entry does not delete the earlier ones. Where it conflicts with them, this entry wins. Detail is in `adrs.md`, `brief.md`, `epics-and-stories.md`, and `parking-lot.md`. Unstated numbers stay in `open-decisions.md`.

No party was run for the open points. `bmad-party-mode` needs the human in the room. No persona voted. The architecture coaching path, memlog, and reviewer gate were not run. ADR ids were kept. New ids are ADR-009 through ADR-016.

Closed in this review:

- Two starting offers, plus a plan table the owner can extend from the microsite. The pay screen shows the active plans. Pack Evento is ARS 1000 for 24 hours, one commerce admin, with the face copy "Un día, un usuario. Ideal para la fiesta o el show." Pack Mes is ARS 5999 for 30 days, one admin and up to four Puntos de Alerta, with a 21-day trial that starts when Mercado Pago is connected. Help is included. The benefit list was not defined.
- Prepaid quantity packs (50, 100, 500, 1000+) are discarded. The single price and the 14-day trial are withdrawn.
- Access is a date, prepaid, paid by a Mercado Pago payment link into the owner's account, the one set in the services panel. No preapproval, no automatic debit, no extra debit layer, no cashback, no coupons. Five days before the date, an email and a banner warn them, except on Pack Evento. On the date, the web blocks. A payment while days remain continues without a gap. A payment after the block starts at payment. The owner does not mark payments by hand.
- A referral code only. The first Pack Mes paid with that code makes the active referrer's next Pack Mes free, once. A blocked referrer is not unblocked and the free month is not stored. Pack Evento does not grant it. The invited commerce pays full price.
- The primary client is the commerce webapp on the computer where stock and collections already run. The phone is Pack Evento. No native app. The second role is Punto de Alerta. ~~Admin sign-in is email, password, and a code to that email.~~ Superseded 24 Oct 2026: commerce admin identifies with Auth0 (ADR-018). The alias is chosen once. A Punto de Alerta enters by a single-use short-lived link (usuario/clave, not Auth0 SSO). WhatsApp only carries the message.
- Superadmin is the product owner on a separate microsite. The first account comes from server configuration. No public signup. No Auth0 and no other third party for that login. Mercado Pago OAuth does not open the microsite.
- The last 10, the poll, and the browser notification stay. While the webapp is open, a new transfer appears immediately. No hub method was named.
- A weekly cap exists, is typed, and is unset. Hangfire 1.8.25 is already installed, dashboard only in Development at `/hangfire`, with no jobs. The poll stays `TransferPollWorker`. MediatR and a generic repository stay out.
- Help pages in `web/help/` are the corpus. A failed index refresh leaves production up. JEV's URL, auth, and index name were not defined. SystemOne, the provider leader, and the LLM service wait for the human.
- The services panel has four named cards: Mercado Pago platform application, mail, browser-push credentials, and WhatsApp with no key. The owner sees that a secret is set, the last four characters, and an expiry. The full secret is not shown.
- The brand manual is a design reference, not a module and not a gate. The helpdesk and the log-reading Cursor agent stay parked. They are not an epic.
- Verification stays tests, Swagger, and the browser. This documentation pass does not run that ritual. Nothing was committed.

`Docs/ManualdeMarcayUI.html` was not rewritten. These on-screen sentences are now wrong:

- A 14-day trial, including "prueba gratuita por 14 días" and "Confirmación de los 14 días bonificados iniciados."
- Day 15 as the unpaid cutoff, including "Día 15 sin Pago" and the paragraph that keeps the last list up and says the screen must never block. On the access date the web now blocks. The trial length is 21 days. The new cutoff instant was not named.
- One price, including "un único número comercial de 5999 pesos argentinos" and the pay row that shows only $ 5.999 every 30 days. The pay screen shows the active plans. Pack Evento is the second starting offer. Checkout Pro is no longer the name of that payment. It is a Mercado Pago payment link.
- "cajero" and "frente a la caja." The role is Punto de Alerta. This pack does not talk about cajas.

## Human decision — 23 October 2026

Source: the human, that day. This entry does not delete the earlier ones. Where it conflicts with them, this entry wins. Detail is in `adrs.md`, `brief.md`, `epics-and-stories.md`, `open-decisions.md`, and `parking-lot.md`. The permission comma is `party-permissions-2026-10-23.md`. Nothing was committed. `src/` and `web/` were not edited.

Closed by the human:

- Trial access ends 21 days after Mercado Pago is activated and the account is linked, at that same clock time. The clock starts at that link. It does not end sometime in day 22. ADR-004.
- The hard weekly cap is 500 queries. Reading of "500 (con gap de 50 -10%)": the warning is when 50 remain, at 450 used, and 500 is the wall; that is the KISS reading of a gap before the wall, and the human can correct it. At 500, new queries and alerts wait until the next week. The 22 Oct line said new transfers and alerts; this entry uses the 23 Oct words, queries and alerts. No overage charge. ADR-007.
- An accumulable recharge of 500 queries may be evaluated later from real application telemetry. The limit is theoretical. The human believes 500 will be enough. Not in scope. Not a story.
- Pack Mes help is a chatbot assisted by the AI copilot. The person can ask a question. Pack Evento help is a chatbot with a static menu of predetermined options. The person cannot ask a free question. The plan-table flag is `EnableCopilotHelp`. The human's phrase was enable-copilot-help. Pack Mes on, Pack Evento off. What "ayuda" includes is that behavior. ADR-009 and ADR-017.
- The copilot follows `help-copilot-technical-spec.md` where that spec decides, with Pscielo.Llm used only as a pattern. Where they disagree, the spec wins. No new host, queue, or provider leader. ADR-017.
- The invitation link lives 1 hour and stays single-use. The email sign-in code lives 1 hour. The human said both lifetimes were already defined as 1 hour. The code length was not stated.
- The five-day notice and the referral bonus run on Hangfire. The Mercado Pago poll stays on `TransferPollWorker`. No other Hangfire job. ADR-011.
- The commerce admin has dominion over their own tenant account. They can query transaction history, export a CSV report, and see the grand total of transfers by day, by month, and by a date range.
- A Punto de Alerta sees a configurable count of records. The typed setting `AlertPointRecordCount` defaults to 10. Mercado Pago account features are hidden from that role.

Party recommendation, not yet adopted that afternoon. Superseded the same evening:

- The human's Spanish list for a Punto de Alerta included the record count, hiding Mercado Pago account features, and "ver detalle de la transacción". The comma can mean they may see a transaction's detail, or that the detail is one of the features to hide. The party recommends the small reading: the alert point sees the last N rows, default 10, and can open the detail of one of those rows, and cannot open history, the CSV, the totals, or Mercado Pago settings. The skill does not let the room replace the human, so this afternoon text is not itself the lock.

Still open that afternoon, superseded the same evening: the week boundary is not chosen, Monday or a rolling 7 days.

## Human decision — 23 October 2026, evening

Source: the human, that evening. This entry does not delete the earlier ones. Where it conflicts with the 23 Oct entry above, this entry wins. Nothing was committed. `src/` and `web/` were not edited.

Closed by the human:

- Party recommendation adopted. A Punto de Alerta sees the last N rows (default 10) and may open the detail of one of those rows, because Mercado Pago sometimes does not show the sender name. They cannot open history, a CSV, the totals, or Mercado Pago account settings. The commerce admin keeps history, a CSV, and the totals by day, by month, and by a date range. ADR-013. Story 7.5. `party-permissions-2026-10-23.md`.
- The usage week is seven consecutive days starting from the cutoff date (fecha de corte). It is not a calendar week and not Monday. ADR-007. Story 10.1.

Still open that evening, superseded the same night: The human did not define which instant "fecha de corte" is: subscription start, Mercado Pago link, first day the cap counter runs, or the access end date. The night entry closes it.

## Human decision — 23 October 2026, evening, mail transport

Source: the human, that evening. They were asked whether the mail transport for the sign-in code and the five-day notice is generic SMTP or the SendGrid sender from the old SaaS. They said it is configurable. This entry does not delete the earlier ones. Nothing was committed. `src/` and `web/` were not edited.

Closed by the human:

- The mail card on the owner microsite (ADR-012) does not lock SendGrid and does not lock MonsterASP SMTP. One active sender. The choice is a typed setting on that card. The concrete provider value stays unset. Until the microsite screen exists, the same fact is server configuration, not a commerce screen.

## Human decision — 23 October 2026, night

Source: the human, that night, answering the three build questions. This entry does not delete the earlier ones. Where it conflicts with the evening entry, this entry wins. Nothing was committed. `src/` and `web/` were not edited. No bmad-build was started.

Closed by the human:

- The weekly-cap cutoff instant is the moment Mercado Pago is linked. From that instant, seven consecutive days run. The next period starts when the previous one ends. The 21-day trial uses that same start. ADR-007. Story 10.1. Story 5.2.

- The old-UI salvage is adopted as written, Rescatar and Dejar. `party-old-ui-2026-10-23.md`. No new ADR holds that list. Folds that change an existing check are the sentences below. A leave that contradicted nothing already written stays in that file and in this entry.

Folds:

- Login door (story 2.1): "Hola de nuevo", "Inicia sesión en tu cuenta de TransfersCheck", "Dirección de email *", "Contraseña *", "Iniciar sesión", and the show/hide control on the password. Centered card, one column. ~~No Auth0 and no Google on that door. The email code on every login stays (story 7.1).~~ Superseded 24 Oct 2026 by Auth0 course correction (ADR-018): Auth0 Universal Login in; local email-code door withdrawn. Spanish email/password labels remain usable when Universal Login shows that form. The earlier door words Entrá, Correo, and Contraseña are the previous wording.
- Menu: Transacciones, Configurar, Pagos, Salir on desktop, and Cerrar sesión in the menu. The bar shows the email, truncated. The slot beside the logo shows `#alias`. Configurar is Mercado Pago and the Puntos de Alerta. The first-run walk stays. Volver stays. **Intentional product amendment (24 Oct 2026):** the commerce admin menu also keeps **Historial** so story 7.6 (Historial de Transacciones, CSV, totals) stays reachable. The four-item Epic 2 shell list is not a reason to hide that screen; Punto de Alerta still does not see Historial.
- History (stories 4.1 and 7.6, ADR-013): title "Historial de Transacciones" on the admin history only. Columns Fecha, Pagador, Monto, Estado. No Medio column. "Desconocido" when the payer name is empty. Amounts use es-AR and ARS. Dates use `dd/MM/yyyy HH:mm`. Admin history is not capped at 10 rows. The alert-point window stays the last N, default 10.
- Mercado Pago connect labels (story 3.3): "Conectar Mercado Pago", "Re-conectar cuenta", "Conectado", "No conectado", and the confirm "¿Estás seguro que deseas re-conectar tu cuenta de Mercado Pago?".
- Notification button (story 6.5): "Habilitar Notificaciones". The button does not name the mechanism.
- Pay and referral (stories 5.1 and 8.1, ADR-015): labels "Email", "Plan", and "Mi código". No cashback balance.
- One legal sentence (ADR-004): TransfersCheck is an assistance tool, and the user is the only one who verifies that the funds actually landed in the bank or the wallet.

Leaves:

- ~~No Auth0 on the commerce door. ADR-002 already says Auth0 is out. No Google on that door.~~ Superseded 24 Oct 2026 (ADR-018). Owner microsite and Punto de Alerta Auth0 exclusions remain.
- No cashback balance. ADR-004 and ADR-015 already say no cashback.
- No recurring-subscription legal text. Nothing already written asked for that paragraph. ADR-004 records that it is not adopted.
- No Backend Config / Pruebas screen on the commerce webapp. ADR-007.
- No admin password gate. Nothing already written asked for it.
- No Sincronizar. Story 4.1. The poll stays `TransferPollWorker`. The refresh button stays.
- No search. Story 7.6. Day, month, and range stay the admin's finder.
- No Medio column.
- No 10-row cap on the admin history.

The closed screen words stay Llegó for approved, Pendiente for pending, and Rechazado for rejected. The rescued old-code words Aprobado and En proceso do not replace them.

Other rescued labels in the adopted file, including the empty-list sentence, the footer, and the in-page toast, did not change an existing check. They stay in that file.

The first bmad-build boundary is the First build note in `later-research.md`. The owner microsite, the help copilot, Jev, and the production pipeline are not in that build.

## Human decision — 24 October 2026, commerce Auth0 course correction

Source: the human. Corrects the prior "email/password only, Auth0/Google out of scope" lock for the commerce door. This entry does not delete the earlier ones. Where it conflicts with the 23 Oct night leave "No Auth0 on the commerce door" and with ADR-002 / ADR-013 sentences that Auth0 was out of commerce login, this entry wins. Detail is ADR-018. Sprint change proposal: `sprint-change-proposal-2026-10-24-auth0-commerce.md`. Nothing was committed by this documentation pass. Prefer docs before code realign.

Closed by the human:

- Commerce signup and login identify with Auth0 as the main path.
- Simplify that door: SSO via Auth0 Universal Login, or create usuario y contraseña (Auth0 Database connection). Default social SSO provider recorded here: Google. More Auth0 social providers may be enabled later without a new product ADR.
- Exception: Punto de Alerta enters with usuario/clave (invite + local password). Not the Auth0 SSO door. ADR-013, stories 7.3 and 7.4.
- Do not confuse Mercado Pago OAuth (collection-account linking) with login SSO. MP OAuth stays payment-account linking. ADR-004.
- Owner microsite login stays local. No Auth0 there. ADR-014.
- After Auth0 identity succeeds, the API still issues the local HttpOnly Secure `SameSite=None` session cookie. `TenantId` still comes only from the server session. ADR-002, ADR-008.
- The previous local "email + password + 6-digit email code on every commerce admin sign-in" door (story 7.1 as previously written) is withdrawn for the Auth0 path. Extra verification/MFA, if wanted, is Auth0 Universal Login configuration.

Leaves that stay:

- No Auth0 on the owner microsite (ADR-014).
- No Auth0 SSO door for Punto de Alerta (ADR-013).
- Mercado Pago OAuth is not the IdP.

Defaults recorded so implementers are not guessing (reversible by the human):

- Auth0 Universal Login + Database connection + Social (Google default).
- Bridge pattern: Auth0 IdP → API local session cookie.
- Story 7.1 becomes Auth0-owned verification, not a local Código field on the commerce door.
