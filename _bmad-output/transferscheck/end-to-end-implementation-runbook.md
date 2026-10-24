# TransfersCheck first-build implementation runbook

24 October 2026. This file is the executable run. It does not implement product code. One wave is one iteration. Do not reopen an ADR. Do not add a story. Do not read `docs/Archive/**` or `later-research.md`. Do not open SimpleCheckMercadoPago.

The step shape matches one unattended bmad-build-auto iteration: this step is the intent, the red test is written and failing before production code, the green code stays inside the project and seam named here, verify is the command named here, and a stop listed here is a halt. Do not run the skill to re-decide architecture, ports, providers, lengths, hub methods, cron expressions, or what a consulta is. Do not convene party-mode to fill a stop listed here.

## Goal and done

Done is waves 1 through 22 of the first build. Wave 1 is already closed. Waves 2 through 22 are this run. After the last wave that is allowed to finish, `dotnet test tests/TransfersCheck.Tests/TransfersCheck.Tests.csproj` is green, and Swagger or a browser pass has been exercised only where that wave's story already requires it.

Epics 11 and 12 are not done. They are the tail in "Not in this run".

## Verified baseline

Checked against `first-build-prompt.md`, `epics-and-stories.md`, `open-decisions.md`, `adrs.md`, and a light pass of `src/` and `tests/`. `docs/Archive/**` was not read.

- Wave 1 is closed. Commit `7401c03` ("Stand up the API and PWA as separate deployables with a tenant filter") covers stories 1.1, 1.2, and 1.5. Evidence: `tests/TransfersCheck.Tests/Epic1PlatformTests.cs` (`Core_does_not_reference_ef_core_or_aspnet`, `Backend_is_three_projects_and_the_image_excludes_the_pwa_and_sql`, `Composition_maps_only_mercadopago_and_the_four_ports`, `Swagger_serves_the_contract_and_cors_allow_lists_the_ui_origin`).
- The same commit already contains the story 1.3 tenant filter and `A_read_for_one_merchant_returns_only_that_merchants_rows`. That does not close wave 2. Story 1.4 is still the withdrawn lock.
- HEAD is `155f648d2185c289a81b7fc94d169d69520880b5` ("Notify approved accounts from the installable PWA"). Commits after `7401c03` (`0c69c7f`, `28b93ee`, `95500bc`, `ab221ee`, `155f648`) already contain login, Mercado Pago connect, the last 10, one price, and the PWA, written against checks the live plan retired. They are not closed waves. Later waves realign that code. Do not skip a wave because the files exist.
- `src/TransfersCheck.Core/Options.cs` still defaults `TrialDays` to 14 and `PriceAmount` to 5999. `Typed_configuration_carries_the_locked_commercial_numbers` still asserts those two numbers.
- Dirty product files are ahead of the plan and are not closed waves: `src/TransfersCheck.Api/Program.cs` and `TransfersCheck.Api.csproj` reference Hangfire.AspNetCore 1.8.25 and Hangfire.SqlServer 1.8.25 and map `/hangfire` only in Development; `src/TransfersCheck.Api/Auth/AuthenticatedDashboardFilter.cs` is untracked; no Hangfire job is registered (`RecurringJob` is absent); `AccountEndpoints.cs` and `Epic2LoginTests.cs` contain uncommitted sign-out; `web/src/App.tsx` still shows the retired door and menu words (Correo, Entrá, Lista, Configurar usuarios, Extender acceso) and has Salir. `web/e2e/onboarding.spec.ts` still fills the label "Correo".
- `IMerchantStore` already has many methods. Do not split it. Do not add methods to it. Ports that already exist beside the four story 1.5 names (`ITokenProtector`, `IPollLock`, `IPushSender`, `ICheckout`) stay. New behavior does not go through a new method on `IMerchantStore`.
- On 24 October 2026 this machine's `dotnet --list-sdks` listed only `9.0.316` and `9.0.319`. `global.json` requires SDK `10.0.402` with `rollForward: latestFeature`. Projects target `net10.0`.

## Global rules

- Tests first. Write the wave's test, run it, see it fail for the assertion named here, then write production code, then see the suite green.
- One wave per iteration. Do not start the next wave in the same iteration.
- New behavior after wave 1 is a Core use case plus a small port. It is not more methods on `IMerchantStore` and not more static methods on an endpoint class. Do not split `IMerchantStore`. Do not add MediatR. Do not add a generic repository. Do not add a key-value configuration bag.
- Do not choose an open item. Email-code length, the owner's Mercado Pago account, a SignalR hub method name, a mail provider, a cron expression, Firebase project values, and what a consulta is are not guesses. The stops below are the halt.
- A missing number, provider, host, copy, or permission is a stop. Closed numbers stay closed: cap 500, warning gap 50 (warn at 450), `AlertPointRecordCount` default 10, `EmailCodeLifetime` 1 hour, `InviteLinkLifetime` 1 hour, trial 21 days from the Mercado Pago link, seven-day periods from that same link, Pack Evento ARS 1000 and 24 hours, Pack Mes ARS 5999 and 30 days.
- English-only code, tests, and commit messages. This runbook does not itself commit. When the executing agent commits, commit only after that wave's verify is green, one wave at a time, message about why. Do not stage a file the wave did not change. Do not stage `.env`, user-secrets, service-account JSON, or appsettings that contain credentials. Empty typed keys the pack left unset may be committed empty. Push only if an upstream exists. If there is no upstream, stop and ask. Do not invent a remote.
- Known dirty files listed in the baseline may stay dirty until the wave that names them. Do not clean the tree. Do not stage them early. Unknown new dirt stops the wave. Ask. Do not stage it.
- Do not build the chatbot, the help index, the owner microsite, the services panel, or a production pipeline. Store `EnableCopilotHelp` when story 5.1 says so. Do not build what the flag turns on.
- Verify command, from `parking-lot-waves.md` and `saas-reconciliation.md`, for every wave that reaches green: `dotnet test tests/TransfersCheck.Tests/TransfersCheck.Tests.csproj`. The SQL fixture inside `Epic1PlatformTests.cs` uses `Server=localhost\SQLEXPRESS;Database=TransfersCheckTests;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True`. Do not change that string. If the command fails because that instance is down, stop and report it.
- Swagger and the browser run only after that wave's production code exists, and only when the step below includes them. Swagger is the existing check `Swagger_serves_the_contract_and_cors_allow_lists_the_ui_origin` against `/swagger/v1/swagger.json`. The browser command that already exists is `npm run e2e` in `web/`. `scripts/dev-up.ps1` is the existing local start script when a story requires a person to exercise a screen. A screenshot is not the pass.
- `net10.0` stays the target. Do not retarget to `net9.0`. Do not add an SDK install as a design change.

## Step 0 — test order in the first-build prompt

Status: done on 24 October 2026. Applied in `_bmad-output/transferscheck/first-build-prompt.md`. Do not edit it again.

Previous sentence:

> Inside a wave, unit tests, Swagger, and the browser are read-only checks and may run in parallel only after that wave's code exists.

Replacement sentence now in that file:

> Inside a wave, that wave's tests are written and failing before that wave's production code. Swagger and the browser are read-only checks and may run in parallel only after that wave's code exists.

The stories were not rewritten for this rule.

## Step 0b — story 1.5 is not a closed port list

Status: done on 24 October 2026. Applied as one sentence on story 1.5 in `_bmad-output/transferscheck/epics-and-stories.md`. Do not edit the story again. ADR-007 and ADR-010 were not edited.

Sentence now in the story:

> Those four names are the current set, not a closed set: new behavior after wave 1 is a Core use case and a small port, not more methods on `IMerchantStore` and not more static endpoint methods, and this story does not split `IMerchantStore`.

Wave 5 does not repeat this edit. It obeys the sentence.

## Execution stop — SDK, before wave 2

This is not a wave and not a design change.

Re-run `dotnet --list-sdks`. `global.json` asks for SDK `10.0.402` with `rollForward: latestFeature`, and the projects target `net10.0`. On 24 October 2026 the machine listed only SDK 9 (`9.0.316`, `9.0.319`).

If no 10.0 SDK is listed, stop. Do not retarget. Do not start wave 2's red test. Do not record an install choice in an ADR or in `decisions.md`. Report the `dotnet --list-sdks` output and the `global.json` version.

When a 10.0 SDK that satisfies `global.json` is present, start at wave 2.

## Wave 2 — Stories 1.3, 1.4

Completed by moving the existing caller integers out of CommercialOptions until wave 9, no new commercial number chosen.

**Outcome.** A tenant read still returns only that tenant's rows, and the process reads the live typed settings.

**Precondition.** Step 0 and Step 0b are done. The SDK stop above is clear. Wave 1's tests stay green.

**Red.** Story 1.3 already has `A_read_for_one_merchant_returns_only_that_merchants_rows` in `tests/TransfersCheck.Tests/Epic1PlatformTests.cs`. Do not rewrite it. The failing test is the existing `Typed_configuration_carries_the_locked_commercial_numbers` in that same file. Change it before touching `Options.cs`.

- Given the API configuration, when the process starts, then `WeeklyQueryCap` is 500 and `WeeklyQueryWarningGap` is 50.
- And `AlertPointRecordCount` defaults to 10.
- And `EmailCodeLifetime` is 1 hour and `InviteLinkLifetime` is 1 hour.
- And page size for the last-10 list is 10.
- And each of those is a typed property.
- And the test does not assert `TrialDays` of 14 or `PriceAmount` of 5999.

Keep the assertions the live stories still close and this test already has: poll interval 5 minutes, button cooldown 60 seconds, and an empty `Onboarding:MercadoPagoVideoUrl`. Do not assert a configuration subsystem.

**Green.** Add the typed properties on the existing options types in `src/TransfersCheck.Core/Options.cs`. No new project. No key-value bag. Remove `TrialDays` and `PriceAmount` from `CommercialOptions` so those two numbers cannot stay locked. Do not put Pack Evento, Pack Mes, 21 days, 1000, or 5999 on `CommercialOptions`. Plan price and duration belong to the plan table in wave 9.

Compile realignment in this wave, with no new commercial rule: `src/TransfersCheck.Api/Billing/BillingEndpoints.cs`, `src/TransfersCheck.Api/Connections/ConnectionEndpoints.cs`, `TransfersApiFactory` in `Epic1PlatformTests.cs`, and `tests/TransfersCheck.Tests/Epic5BillingTests.cs`. Those Epic5 tests currently lock the withdrawn single price and the day-15 stop. Change them only so they no longer require `TrialDays` 14 or `PriceAmount` 5999. Do not make them assert stories 5.1, 5.2, or 5.5.

**Verify.** `dotnet test tests/TransfersCheck.Tests/TransfersCheck.Tests.csproj`. No Swagger. No browser. Stories 1.3 and 1.4 do not add a screen or a new HTTP document.

**Stop.** If a production call site cannot compile without a replacement trial length or price, stop. Do not pick a number.

**Realign.** The tenant filter in `src/TransfersCheck.Infrastructure/TransfersCheckDb.cs` already exists. Do not add a second filter. `Options.cs` and the commercial-numbers test are the work.

## Wave 3 — Story 1.6

Completed on 24 October 2026. Hangfire.AspNetCore/SqlServer 1.8.25, jobs `five-day-notice` and `referral-bonus` registered with empty bodies via `IRecurringJobManager` + `Cron.Never()` (identity only, no product schedule), dashboard Development-only at `/hangfire`, poll remains `TransferPollWorker`. Evidence: `Hangfire_registers_only_the_five_day_notice_and_referral_bonus_jobs`.

**Outcome.** Hangfire.AspNetCore 1.8.25 and Hangfire.SqlServer 1.8.25 are referenced, the five-day notice job and the referral-bonus job are registered with empty bodies, the dashboard is only in Development at `/hangfire`, and the poll stays `TransferPollWorker`.

**Precondition.** Wave 2 is green.

**Red.** Add a failing test in `tests/TransfersCheck.Tests/Epic1PlatformTests.cs`.

- Given TransfersCheck.Api, when the process starts, then the project references Hangfire.AspNetCore 1.8.25 and Hangfire.SqlServer 1.8.25.
- And the dashboard is mapped only in Development, at `/hangfire`.
- And the five-day notice is a Hangfire job and the referral bonus is a Hangfire job.
- And no other Hangfire job is registered.
- And the Mercado Pago poll is still `TransferPollWorker`, not a Hangfire job.
- And the job bodies are empty: the test does not require an email, a banner, or a free month.

**Green.** `src/TransfersCheck.Api` registers the two jobs and maps the dashboard. The poll registration stays `TransferPollWorker`. Do not fill the job bodies. Do not move the poll onto Hangfire. Do not add a third job.

**Verify.** The unit-test command. Then, after the code exists, request `/hangfire` on a Development host and confirm the two jobs are present and the poll is not one of them. No Swagger. No commerce screen.

**Stop.** ADR-011 does not add a cron expression or a scheduler design. If the only way to register a job requires inventing a cron expression, stop. Do not invent one. Waves 11 and 20 call these same jobs. They do not add a second registration and they do not add a cron either.

**Realign.** The dirty `Program.cs`, `TransfersCheck.Api.csproj`, and untracked `AuthenticatedDashboardFilter.cs` are this wave's surface, not a closed wave. Story 1.6 does not require a dashboard login. Do not grow `AuthenticatedDashboardFilter` into a new product rule, and do not require a commerce session for `/hangfire`.

## Wave 4 — Stories 2.1, 2.3

Completed on 24 October 2026. Adopted door copy, menu (Transacciones, Configurar, Pagos, Salir / Cerrar sesión), empty `#alias`, sign-out ends live session. Evidence: Epic2 login tests + `web/e2e/door.spec.ts` and updated smoke/onboarding.

**Outcome.** The commerce door and one live session. Signed-in header and menu use the adopted words. Login and signup show the wordmark and no menu.

**Precondition.** Wave 3 is green.

**Red.** Existing tests in `tests/TransfersCheck.Tests/Epic2LoginTests.cs`: keep `Sign_up_sets_a_cross_site_cookie_and_ignores_a_client_tenant` and `A_new_login_replaces_the_previous_session` if they already match the bullets below. The uncommitted `Sign_out_ends_the_live_session` is not a closed wave. Change `web/e2e/onboarding.spec.ts` first so it no longer types the label "Correo".

- Given a commerce signup and sign-in, when the API sets the session, then the cookie is HttpOnly, Secure, and `SameSite=None`, and `TenantId` comes from the server session. A client-supplied tenant is ignored.
- Given one login account, when a second sign-in happens on that same account, then the previous session no longer works.
- Given a live session, when the person leaves with Salir or with Cerrar sesión, then that session ends and the screen is the login screen.
- Given the login and signup screens, when they render, then they show the wordmark and no menu, and the door shows "Hola de nuevo", "Inicia sesión en tu cuenta de TransfersCheck", "Dirección de email *", "Contraseña *", "Iniciar sesión", and a show/hide control on the password.
- And the door has no Auth0 and no Google.
- Given a signed-in header, when the menu opens, then the entries are Transacciones, Configurar, Pagos, and Salir, and leaving uses Cerrar sesión.
- The `#alias` slot stays empty. Story 7.2 fills it. This wave does not choose the email-code length. Story 7.1 adds the code.

**Green.** Realign `src/TransfersCheck.Api` session issuance and `web/`. The seam is the existing session and account flow. Do not add a method to `IMerchantStore` for the menu. Do not add an identity provider.

**Verify.** The unit-test command. After the code exists, `npm run e2e` in `web/` for the door and the menu this wave changed. Exercise sign-in, a second sign-in replacing the first, and Salir / Cerrar sesión returning to the login screen. No new Swagger document. The existing session routes stay on the current Swagger check.

**Stop.** None in this wave. Do not stop to choose the email-code length. Do not build story 7.1 here.

**Realign.** Committed login is `0c69c7f`, plus the dirty sign-out edits. The visible copy is still the retired door. Change the copy. Do not rewrite the cookie test.

## Wave 5 — Stories 2.2, 2.4

Completed on 24 October 2026. `LoginAccountCap` + `ITenantPlanCode` (no new `IMerchantStore` methods); Pack Evento / unset = admin alone; Pack Mes = four Puntos de Alerta; revoke unchanged. Dev-only `PUT /api/dev/plan-code` until wave 9. Evidence: `Pack_mes_refuses_a_fifth_punto_de_alerta_and_pack_evento_refuses_a_second_account` + `web/e2e/configurar.spec.ts`.

**Outcome.** Configurar enforces the plan account cap and revokes a login. Pack Evento is the admin alone. Pack Mes is one admin and at most four Puntos de Alerta.

**Precondition.** Wave 4 is green. Step 0b is already in story 1.5. Obey it. Do not edit the story again.

**Red.** Replace `A_merchant_stops_at_five_login_accounts` in `tests/TransfersCheck.Tests/Epic2LoginTests.cs`. That test is the withdrawn "any paying merchant has five logins" check. `Revoke_ends_the_session_and_deletes_the_push_token` may stay if it matches the revoke bullets. Write the cap test failing before production code.

- Given Pack Mes, when the admin already has four Punto de Alerta accounts, then a fifth Punto de Alerta is refused. The commerce has one admin.
- Given Pack Evento, when a second account is requested, then it is refused. The only account is the commerce admin.
- And there is no per-seat price and no permission matrix. The second role is named Punto de Alerta.
- Given a revoked Punto de Alerta, when the revoke is saved, then that session ends, the FCM token for that account is deleted, and polling for that device stops.
- And the owner account is not revoked by that action.
- And the invite itself is not built here. Stories 7.3 and 7.4 own it. There is no pay screen in this wave.

**Green.** A Core use case decides the cap from the plan code and the current accounts. A small port supplies the plan code and the accounts. Do not add methods to `IMerchantStore`. Do not split `IMerchantStore`. The screen is Configurar in `web/`. Do not build the pay screen or the plan-table editor. The plan code this wave stores is only enough for the cap. Prices stay in wave 9.

**Verify.** The unit-test command. After the code exists, exercise Configurar in the browser: Pack Evento refuses a second account, Pack Mes refuses the fifth Punto de Alerta, and revoke ends that login without removing the owner. `npm run e2e` in `web/` if the spec covers Configurar; otherwise exercise that screen with `scripts/dev-up.ps1`. No Swagger change unless a route the cap uses is new, in which case extend the existing Swagger test so the route is on `/swagger/v1/swagger.json`.

**Stop.** If the cap cannot be tested without inventing a price, a permission, or a seat fee, stop. The caps and the role name are already closed.

**Realign.** The five-account endpoint behavior in `AccountEndpoints.cs` is the old rule. Replace it. Do not keep "stop at five" as a third rule.

## Wave 6 — Stories 3.1, 3.2, 3.5, 3.7

Completed on 24 October 2026. PKCE connect and one-merchant link kept; setup checklist cleaned of withdrawn Checkout Pro / price / trial lock; `Operator_setup_is_the_pkce_checklist_with_no_secret_values` locks the film script and Data Protection key path.

**Outcome.** Mercado Pago connect by authorization code with PKCE, one merchant per account, the merchant never creates an application, and `mp-authorization-setup.md` has the operator checklist and no secret values.

**Precondition.** Wave 5 is green.

**Red.** Keep these tests in `tests/TransfersCheck.Tests/Epic3ConnectTests.cs` where they already match: `Connect_uses_pkce_and_an_expired_state_is_rejected`, `A_mercado_pago_account_already_linked_is_refused`. Add a failing test for the gaps.

- Given connect, when the merchant starts it, then the flow is authorization code with PKCE, and `state` and the code verifier are stored with an expiry. A row past its expiry is rejected.
- Given a Mercado Pago account already linked to a merchant, when a second merchant links it, then the link is refused. Tokens are encrypted at rest. The Data Protection key path comes from configuration.
- Given the merchant, when they connect, then they never create a Mercado Pago application, never paste a client secret, and never upload a JSON. The merchant action is OAuth consent only. One platform application is operator configuration.
- Given `_bmad-output/transferscheck/mp-authorization-setup.md`, when a test reads it, then it is the film script and the checklist: create the platform application, set the redirect URI, enable PKCE, put the client id and client secret in user-secrets or appsettings, rotate any secret that was in the old proof, and confirm with one real OAuth. The file contains no secret values. It is not shown to a Punto de Alerta.

**Green.** Realign the existing connect path in `src/TransfersCheck.Infrastructure` and `src/TransfersCheck.Api/Connections`. The seam is the existing `IMerchantAuthorizer`. Do not add a method to `IMerchantStore`. Client id and client secret stay typed configuration. Do not write secret values into the repo. In `mp-authorization-setup.md`, remove the sentence that locks Checkout Pro and the withdrawn single price and trial. Do not replace it with a new commercial rule.

**Verify.** The unit-test command. No browser product screen beyond the connect action story 3.3 owns in the next wave. No new Swagger document. Confirm the setup file has no secret values.

**Stop.** If a live OAuth confirmation requires a real client secret in the repo, stop. Leave the secret in user-secrets. Do not invent rotated values.

**Realign.** Connect is already committed in `28b93ee`. Realign the tests and the setup file. Do not port a second provider.

## Wave 7 — Stories 3.3, 3.4, 3.6

Completed on 24 October 2026. Adopted connect words / badges / reconnect confirm; trial as status; alerts on checklist; OAuth returns to `/conectar`; empty video hides player. Evidence: Epic3 checklist tests + `web/e2e/checklist.spec.ts` / onboarding.

**Outcome.** The checklist in the browser, the step stored on the server from real state, and the video only when `Onboarding:MercadoPagoVideoUrl` is set. That URL stays unset.

**Precondition.** Wave 6 is green.

**Red.** Keep `The_client_cannot_advance_the_checklist` and `A_video_url_is_returned_and_an_empty_one_stays_empty` in `tests/TransfersCheck.Tests/Epic3ConnectTests.cs` where they match. Change the UI assertions that still use retired words. Failing tests:

- Given a new commerce, when the checklist renders, then it is voseo Spanish in this order: create the account, connect Mercado Pago, activate alerts, trial is running.
- And create-the-account shows done when the account exists. Connect shows one primary button and the numbered help: you must manage that Mercado Pago account; tap Connect; sign in and accept; you return here.
- And the connect words are "Conectar Mercado Pago", "Re-conectar cuenta", the badge "Conectado", and "No conectado". The reconnect confirm is "¿Estás seguro que deseas re-conectar tu cuenta de Mercado Pago?". These words are not a live-channel badge and they are not the Mercado Pago user id.
- And activate-alerts is the notification permission, and on iOS Add to Home Screen, on this same checklist. The trial line is status. Pay is not a step.
- Given the server, when it stores the step, then it sets the step from real state: the account exists, a `MerchantConnection` exists, and a push token exists. The client has no call that advances the step. When a `MerchantConnection` exists, connect shows done and the last-10 list is reachable. The alerts step shows done when a push token exists for the tenant. There is no chatbot.
- Given an empty `Onboarding:MercadoPagoVideoUrl`, when the connect step renders, then the player is hidden and the numbered steps remain. A non-empty URL shows an embed or a link. Skipping the video does not block the connect button. Leave the URL unset. Do not build story 11.1.

**Green.** Realign `web/` checklist copy and the existing checklist endpoint. The seam is the existing connection and push-token state. Do not add a method to `IMerchantStore` for a client-advanced step. Do not add a chatbot.

**Verify.** The unit-test command. After the code exists, exercise the checklist in the browser with the video URL empty: the four lines, the connect words, no pay step, and no player. `npm run e2e` in `web/` after the spec expects those words.

**Stop.** None. Do not invent a video URL. Do not build the help page.

**Realign.** Checklist behavior is already committed. The copy and the "no client advance" rule are the work. Do not treat the dirty `App.tsx` menu as this story.

## Wave 8 — Stories 4.1, 4.2, 4.3, 4.4

Completed on 24 October 2026. Columns Fecha/Pagador/Monto/Estado; Llegó/Pendiente/Rechazado; Desconocido for empty payer; Actualizar ahora; no Sincronizar; poll stays TransferPollWorker. Evidence: Epic4 tests + `web/e2e/transfers.spec.ts`.

**Outcome.** The last-10 list, the refresh button, one stored row per payment, and one locked worker. Adopted columns and words. No Sincronizar control. The poll stays. This wave does not build story 9.1.

**Precondition.** Wave 7 is green.

**Red.** Existing tests in `tests/TransfersCheck.Tests/Epic4SyncTests.cs`: `Refresh_keeps_the_last_10_of_the_seeded_types_once`, `Mercado_Pago_429_and_2001_wait_until_the_next_round`, `A_replica_without_the_lock_skips_the_poll`. Extend them, or add a failing test beside them, for the words and the identity. Do not remove the poll.

- Given an entitled merchant, when the worker polls, then the default interval is 5 minutes from configuration, the stored types are `bank_transfer` and `account_money`, and the list length is the last 10. Page size is 10. That length is the counter list and the alert-point window. It is not a cap on admin history.
- And the columns are Fecha, Pagador, Monto, and Estado. There is no Medio column. An empty payer name is Desconocido. Amounts use es-AR and ARS. Dates use `dd/MM/yyyy HH:mm`.
- And there is no Sincronizar control. The poll stays on `TransferPollWorker`. The refresh control stays.
- Given the refresh button, when it runs, then it is the same use case as the clock, the default cooldown is 60 seconds from configuration, and a Mercado Pago 429 or 2001 makes that merchant wait until the next round.
- Given the same (`TenantId`, `Source`, `ExternalId`), when a second insert arrives, then there is still one row.
- Given two replicas, when one does not get `sp_getapplock`, then it skips that poll. The API process hosts the single worker.
- And refresh and the amount are large and thumb-reachable on a phone. Approved shows Llegó, pending shows Pendiente, rejected shows Rechazado.

**Green.** Realign `src/TransfersCheck.Infrastructure/TransferSync.cs`, `src/TransfersCheck.Api/Transfers/TransferPollWorker.cs`, and the list in `web/`. The seam is the existing `ITransferSource`, `IPollLock`, and `IMerchantStore` read that already upserts. Do not add a method to `IMerchantStore`. Do not add a live-push channel. That is wave 21.

**Verify.** The unit-test command. After the code exists, exercise the list and the refresh button in the browser, including an empty payer name and the three status words, and confirm there is no Sincronizar control. Include a narrow phone width for the thumb-reachable refresh and amount.

**Stop.** None. Do not remove the poll in order to make the list look immediate.

**Realign.** Sync is committed in `95500bc`. Realign columns and words. Do not skip the wave.

## Wave 9 — Stories 5.1, 5.2

Completed on 24 October 2026. Plan table (`IPlanCatalog`) seeds Pack Evento / Pack Mes; pay screen labels Email/Plan; Pack Mes trial 21 days from link via `PackMesTrial`; `ConnectedAt` is the usage-period start. Evidence: Epic5 plan/trial tests + `web/e2e/pay.spec.ts`.

**Outcome.** The pay screen shows Pack Evento and Pack Mes from the plan table. The Pack Mes trial runs 21 days from the Mercado Pago link. That link instant is the start story 10.1 will use.

**Precondition.** Wave 8 is green.

**Red.** Replace what remains of `The_pay_screen_reads_the_configured_price` in `tests/TransfersCheck.Tests/Epic5BillingTests.cs`. That test locked one configured amount. New failing assertions:

- Given Pack Evento and Pack Mes are active plans, when the admin opens the pay screen, then Pack Evento shows ARS 1000, 24 hours, and the face copy "Un día, un usuario. Ideal para la fiesta o el show."
- And Pack Mes shows ARS 5999 and 30 days.
- And the screen is not one fixed number, and it does not offer a quantity pack of 50, 100, 500, or 1000+.
- And the table can hold another plan. Pack Mes has `EnableCopilotHelp` on. Pack Evento has it off. Do not build story 11.3 or the chatbot.
- And the account row label is "Email", the active pack label is "Plan", and the card is not titled "Suscripción".
- Given signup without Mercado Pago activated and the account linked, then the trial has not started.
- When Mercado Pago is activated and the account is linked on Pack Mes, then the clock starts at that link, and access during the trial ends 21 days after that link at that same clock time. It does not end sometime in day 22.
- And that same link instant is stored as the start of the seven-day usage periods. Do not build the cap. That is wave 22.

**Green.** A plan table read by the pay screen. Core holds the plan rows and the trial clock from the link instant. A small port persists the plan and the link instant. Do not add methods to `IMerchantStore`. Do not put the prices back on `CommercialOptions`. Store the flag only. `web/` shows the screen. Do not build the microsite editor.

**Verify.** The unit-test command. After the code exists, exercise the pay screen in the browser: both packs, the face copy, the labels Email and Plan, and no "Suscripción" title. Swagger includes the plan read if it is a new route, by extending the existing Swagger test.

**Stop.** None for the numbers. They are closed. If the screen cannot be shown without the owner microsite, stop and do not build story 12.2.

**Realign.** `ab221ee` and `BillingEndpoints.cs` are the single-price screen. Replace that behavior. Do not keep a day-15 trial.

## Wave 10 — Stories 5.3, 5.6

Completed on 24 October 2026. `ICheckout.CreatePaymentLinkAsync`; owner `Checkout:AccessToken` empty in appsettings; webhook extends with gap rule; browser return does not move access; no cashback/coupon. Evidence: `Payment_link_extends_access_without_cashback_and_return_does_not` + `web/e2e/pay.spec.ts`.

**Outcome.** The prepaid Mercado Pago payment link. No preapproval, no automatic debit, no cashback, no coupon. The browser return does not itself move the access date. The owner account stays unset.

**Precondition.** Wave 9 is green.

**Red.** Realign `Webhook_extends_access_and_the_browser_return_does_not` in `tests/TransfersCheck.Tests/Epic5BillingTests.cs`. It currently expects a body that contains `5999` and a checkout token. Change it before production code so it matches the bullets below and does not require a filled owner account.

- Given the admin pays, then the payment is a Mercado Pago payment link. It is not the commerce collection account.
- And there is no preapproval, no automatic debit, and no extra debit layer. The owner does not mark the payment by hand.
- And the browser return does not itself move the access date.
- When they pay while days remain, then the new period continues without a gap.
- When they pay after the block, then the new period starts at payment.
- Given days left on a period, when the period ends or they pay again, then those days are not returned as money. There is no cashback and no discount coupon.
- And the owner's Mercado Pago account stays unset. The test does not require story 12.3.

**Green.** The existing `ICheckout` seam creates a payment link. Do not add a method to `IMerchantStore`. Do not build the services panel. Leave the owner account key empty. Do not put a token in the repo. Do not add a coupon field or a cashback balance.

**Verify.** The unit-test command. After the code exists, exercise the pay action and the browser return in the browser and confirm the access date does not move on return. Swagger lists the payment-link route if it changed, via the existing Swagger test.

**Stop.** If the payment link cannot be built while the owner account stays unset, stop. Point at story 12.3 and `open-decisions.md` (the owner microsite is not this build). Do not build the panel. Do not choose an account.

**Realign.** The committed checkout path is the old single price. Realign it to the link and the gap rule. Do not treat a green webhook test that still locks `5999` as this story.

## Wave 11 — Story 5.4

Completed on 24 October 2026. `FiveDayNotice` + `IMailSender`/`NullMailSender` + `IFiveDayNoticeStore`; Hangfire `five-day-notice` job body filled; billing `fiveDayWarning` banner. Evidence: `Five_day_notice_emails_pack_mes_and_skips_pack_evento` + `web/e2e/notice.spec.ts`.

**Micro-step, before any production code.** A small mail port is allowed. The test uses a fake port. Do not pick the mail provider. ADR-012 leaves the concrete provider unset and does not lock SendGrid or MonsterASP SMTP. Do not build the mail card on the owner panel. Do not add a method to `IMerchantStore` for mail. Do not register a second Hangfire job. The job identity is the empty five-day job wave 3 registered.

**Outcome.** Five days before the access date, a plan other than Pack Evento sends an email and the web shows a banner. Access still works.

**Precondition.** Wave 10 is green. The wave 3 job exists and its body is still empty.

**Red.** A failing test in `tests/TransfersCheck.Tests/Epic5BillingTests.cs` with a fake mail port.

- Given a plan other than Pack Evento, when five days remain before the access date, then the fake port records one email and the web shows a banner.
- And access still works. New transfers and alerts still run.
- And the notice runs by invoking the Hangfire job wave 3 registered. It is not `TransferPollWorker`.
- Given Pack Evento, then this warning is not sent.
- And the test does not assert a provider name, an SMTP host, or a SendGrid key.

**Green.** Fill the body of the existing five-day job. Core decides "five days remain, and the plan is not Pack Evento". Infrastructure binds the fake in tests and leaves the real sender unbound. `web/` shows the banner. Do not add a cron. Do not register another job.

**Verify.** The unit-test command. After the code exists, exercise the banner in the browser while access still works. No Swagger change for the provider.

**Stop.** If the email cannot be tested without choosing a provider or putting a secret in the repo, stop. Point at ADR-012. The fake port is enough to go green.

**Realign.** Do not create a second notice path beside the wave 3 job.

## Wave 12 — Stories 5.5, 5.7

Completed on 24 October 2026. `AccessBlock` + billing `blocked`; web access-block screen; Pack Evento 24h from link; Pack Mes blocks at trial end. Evidence: `Access_date_blocks_the_web_and_stops_sync_for_pack_mes_and_pack_evento` + `web/e2e/block.spec.ts`.

**Outcome.** The web blocks on the access date. Pack Evento ends at 24 hours under that same block.

**Precondition.** Wave 11 is green.

**Red.** Replace what remains of `Day_15_stops_sync_and_the_list_stays` in `tests/TransfersCheck.Tests/Epic5BillingTests.cs`. That test is the withdrawn day-15 stop that leaves the list up.

- Given the access date has been reached and they have not paid, when someone opens the web, then the web blocks until they pay, and new transfers and alerts stop.
- And for an unpaid Pack Mes trial, that access date is the instant in story 5.2 (21 days after the link, at that same clock time).
- Given Pack Evento has started, when 24 hours have passed, then access ends under the same block. The five-day warning does not apply.

**Green.** Core decides the block from the access instant. The existing entitlement seam may stay if the use case calls it. Do not add a method to `IMerchantStore`. `web/` blocks. Do not invent a new stop copy beyond the story. Do not leave the list usable for new transfers after the block.

**Verify.** The unit-test command. After the code exists, exercise the blocked web in the browser for an unpaid Pack Mes trial and for Pack Evento after 24 hours.

**Stop.** None. The instants are closed. Do not reintroduce day 15 or day 22.

**Realign.** The committed day-15 behavior is the old check. Replace it.

## Wave 13 — Story 6.1

Completed on 24 October 2026. Manifest/theme/icon on `web/`; API image still excludes PWA. Evidence: `The_pwa_is_installable_from_the_ui_origin`.

**Outcome.** The installable PWA is built from `web/`. The API does not serve it.

**Precondition.** Wave 12 is green.

**Red.** Keep `The_pwa_is_installable_from_the_ui_origin` in `tests/TransfersCheck.Tests/Epic6PushTests.cs` if it already matches. Otherwise change it so it fails first on these assertions.

- Given the client, when it is installed, then it is one React PWA built from `web/`. TransfersCheck.Api does not serve it. The service worker is scoped to the UI origin. Android install is through Chrome.
- And the API image and the API host do not contain the PWA assets.
- On iPhone, push works after Add to Home Screen. This wave does not invent Firebase project values. Those stay unset until wave 14, and wave 14 still must not invent them.

**Green.** Realign `web/` manifest and service worker. Do not add a static-file host for the PWA on `TransfersCheck.Api`.

**Verify.** The unit-test command. After the code exists, exercise install from the browser on the UI origin: the manifest is served by `web/`, and the API origin does not serve the PWA. `npm run e2e` in `web/` only for the install surface the spec already opens.

**Stop.** None for the deployable split. It is closed in story 1.1. Do not invent a Firebase project here.

**Realign.** Commit `155f648` and the dirty `web/public/manifest.webmanifest`, `web/index.html`, and `web/public/icon.svg` are this surface. Realign them. Do not mark the wave closed because the PWA files exist.

## Wave 14 — Stories 6.2, 6.3, 6.4

Completed on 24 October 2026. FCM web push on arrived only; foreground beep/notice; `/?first=` tap; Firebase keys empty; no service-account JSON. Evidence: `Arrived_push_uses_fcm_and_pending_rejected_stay_silent`.

**Outcome.** After a transfer is stored, the arrival is heard and shown, and one tap opens that row first. Pending and rejected stay silent. The service-account JSON stays out of the repo.

**Precondition.** Wave 13 is green.

**Red.** `Push_reaches_only_an_approved_account_and_only_when_it_arrived` in `tests/TransfersCheck.Tests/Epic6PushTests.cs` mixes this wave with the withdrawn approval gate. Change it before production code so this wave's assertions are the ones below. Do not add a permission matrix. Wave 15 owns who receives alerts.

- Given a new transfer stored as arrived, when the API sends push, then it is Firebase Cloud Messaging web push over HTTP v1.
- And the service-account JSON is absent from the repo, the client, and the commit. `The_service_account_json_is_not_in_the_repo` stays.
- Given an arrived payment in the foreground, then a short sound and an on-page notice play. Given background or a locked screen, then a system notification with sound shows amount, payer name, and the word for arrived.
- And pending and rejected stay on the list and stay silent.
- Given one tap on the notification, then the last-10 list opens with that row first.
- And the test does not require a Firebase project id, API key, or other project value to be invented. Empty typed keys stay empty.

**Green.** Realign `src/TransfersCheck.Infrastructure/FirebasePushSender.cs` and the arrival UI in `web/`. The seam is the existing `IPushSender`. Do not add a method to `IMerchantStore`. Do not commit a service-account JSON.

**Verify.** The unit-test command, including the service-account absence test. After the code exists, exercise an arrived payment (sound and notice) and a pending or rejected payment (silent) in the browser, and one tap that scrolls or focuses that row first.

**Stop.** If the send cannot be tested without inventing Firebase project values or committing the service-account JSON, stop. A fake sender is enough for the arrival assertions. Do not invent the project.

**Realign.** The committed push path notifies approved accounts. This wave does not keep that gate and does not replace it. Wave 15 states the live rule.

## Wave 15 — Story 6.5

Completed on 24 October 2026. Configurar label `Habilitar Notificaciones`; token register enables `PushApproved`; Punto de Alerta receives. Evidence: `Configurar_habilitar_notificaciones_reaches_punto_de_alerta`.

**Outcome.** Configurar shows Habilitar Notificaciones. A Punto de Alerta receives alerts. The button does not name the mechanism.

**Precondition.** Wave 14 is green.

**Red.** A failing test in `tests/TransfersCheck.Tests/Epic6PushTests.cs`.

- Given the admin's own account, when Configurar renders, then they can turn notifications on with the button "Habilitar Notificaciones".
- And the button does not name push or Firebase as the mechanism. The control does not deactivate the admin.
- Given a Punto de Alerta account, then that person receives alerts.
- And this story does not add a permission matrix. The withdrawn check that push goes only to accounts the admin has approved stays withdrawn.

**Green.** `web/` Configurar button and the existing push seam. Do not add a method to `IMerchantStore` for a permission grid. Do not name the mechanism in the button.

**Verify.** The unit-test command. After the code exists, exercise the button on Configurar in the browser and confirm the visible words do not name the mechanism. Confirm a Punto de Alerta receives an arrived alert.

**Stop.** None. Do not invent a permission.

**Realign.** The old Avisos / approved-account control is the retired behavior. Replace the words and the gate.

## Wave 16 — Story 7.1

Completed on 24 October 2026. Email sign-in code is 6 numeric digits, 1-hour lifetime, via `IEmailCodeStore` + existing mail port. Door gains Código without naming length. Evidence: `Sign_in_requires_the_email_code_and_refuses_it_after_one_hour`, `e2e/email-code.spec.ts`.

**Micro-step, before any production code.** Length locked 24 Oct 2026: 6 numeric digits, lifetime 1 hour. Proceed.

**Outcome.** Every commerce sign-in also requires the email code. The code is 6 digits and lives 1 hour.

**Precondition.** Wave 15 is green. The mail port from wave 11 exists. Reuse it. Do not pick a provider.

**Red.** A failing test in `tests/TransfersCheck.Tests/Epic7AliasTests.cs` (new class in the existing test project, one class per epic, matching the current layout). The fake mail port captures the code the use case emits.

- Given a commerce admin, when they sign in without the code, then the sign-in is refused.
- When they sign in with email, password, and the code the fake port captured, inside 1 hour, then the sign-in is accepted.
- When the same code is used after 1 hour, then it is refused.
- And the test does not assert a character count, a digit count, or an alphabet.

**Green.** A Core use case checks the code and the 1-hour lifetime. The existing mail port sends it. A small port stores the issued code and its expiry. Do not add a method to `IMerchantStore`. The door in `web/` gains the code field without naming a length. Do not add Auth0 or Google.

**Verify.** The unit-test command. After the code exists, exercise the door in the browser: missing code is refused, and the screen does not state a length.

**Stop.** The length stop in the micro-step. Also stop if sending the code requires a real provider. The fake port is enough.

**Realign.** Story 2.1's door stays. This wave adds the code requirement. It does not replace the door copy from wave 4.

## Wave 17 — Stories 7.2, 7.3, 7.4

Completed on 24 October 2026. Alias once via `ICommerceAliasStore`; Pack Mes invite via `IAlertInviteStore` (1h, single-use, `wa.me`); door has commerce+user for Punto de Alerta. Evidence: `Alias_is_confirmed_once_*`, `Pack_mes_invite_*`, `e2e/configurar.spec.ts`.

**Outcome.** The admin confirms the alias once, as `#alias`. Pack Mes gets a single-use invite that lives 1 hour. The Punto de Alerta sets a password from that link. WhatsApp only carries the message, via `wa.me`.

**Precondition.** Wave 16 is green. Wave 5's cap still applies. This wave does not raise the cap.

**Red.** Failing tests in `tests/TransfersCheck.Tests/Epic7AliasTests.cs`.

- Given a new commerce admin, when they choose an alias, see it, and confirm it, then it is shown as `#alias`. A later attempt to change it is refused. The slot beside the logo shows that alias.
- Given Pack Mes and a confirmed alias, when the admin creates a name, then the screen shows `usuario#alias`. The sign-in form is two fields, commerce and user. The hash is not part of a URL.
- And the admin receives a single-use link they can copy or send with `wa.me`. WhatsApp only carries the message. There is no WhatsApp API key.
- And the link lives 1 hour, does not work after that hour, and stays single-use.
- Given an unused link, when the person opens it and sets a password, then that browser is registered and the link does not work a second time.
- When they sign in on another browser, then the previous session ends. The admin can deactivate the account.

**Green.** A Core use case for the one-time alias and a Core use case for the invite. A small port stores the alias and the invite. Do not add methods to `IMerchantStore`. `web/` shows `#alias`, the two-field sign-in, and the `wa.me` link. Do not add a WhatsApp client.

**Verify.** The unit-test command. After the code exists, exercise in the browser: confirm the alias, refuse a second change, create the invite, open it once, set the password, and see the link fail the second time. Confirm the share action is a `wa.me` URL.

**Stop.** If the invite cannot be built without a WhatsApp provider or a key, stop. ADR-012 says WhatsApp has no key and `wa.me` only carries the message.

**Realign.** The header slot that wave 4 left empty now shows `#alias`. Do not add a menu entry.

## Wave 18 — Story 7.5

**Outcome.** A Punto de Alerta sees the last N rows, default 10, and may open the detail of one of those rows. No history, no CSV, no totals, no Mercado Pago settings, no account management, and no title Historial de Transacciones.

**Precondition.** Wave 17 is green. `AlertPointRecordCount` already defaults to 10 from wave 2.

**Red.** A failing test in `tests/TransfersCheck.Tests/Epic7AliasTests.cs`.

- Given a signed-in Punto de Alerta, then they see the last N rows, and N is `AlertPointRecordCount`, default 10.
- And they may open the detail of one of those rows.
- And they receive alerts.
- And Mercado Pago account features are hidden. They cannot open history, a CSV, the totals, or Mercado Pago account settings. They have no control that manages accounts.
- And they do not see the title "Historial de Transacciones".
- And an empty payer name on a row is Desconocido.

**Green.** The existing list use case takes N from the typed setting for this role. A small port reads that window if the current list method cannot express "one of those rows" without a new `IMerchantStore` method. Do not add a method to `IMerchantStore`. `web/` hides the admin-only surfaces for this role. Do not build story 7.6 in this wave.

**Verify.** The unit-test command. After the code exists, sign in as a Punto de Alerta in the browser and confirm the window, the detail of one row, the hidden surfaces, and the absent title.

**Stop.** None. The count and the hidden surfaces are closed.

**Realign.** The last-10 counter list from wave 8 stays for the admin counter. This wave is the alert-point window. Do not cap the admin history here.

## Wave 19 — Story 7.6

**Outcome.** The commerce admin gets that tenant's history, a CSV, and the totals by day, by month, and by a date range.

**Precondition.** Wave 18 is green.

**Red.** A failing test in `tests/TransfersCheck.Tests/Epic7AliasTests.cs`.

- Given a signed-in commerce admin, then they can query that tenant's transaction history, export a CSV, and see the grand total of transfers by day, by month, and by a date range.
- And the history title is "Historial de Transacciones".
- And the columns are Fecha, Pagador, Monto, and Estado. There is no Medio column and no search box.
- And this history is not capped at 10 rows.
- Given a Punto de Alerta, then history, the CSV, and the totals stay refused. That check remains story 7.5. Do not weaken it.

**Green.** A Core use case for history, CSV, and the three totals. A small port reads that tenant's transfers for the range. Do not add methods to `IMerchantStore`. Do not add a search parameter. `web/` shows the title and the columns. Another tenant's rows stay behind the existing tenant filter.

**Verify.** The unit-test command. After the code exists, exercise the history screen in the browser as the admin: the title, the columns, a range total, a CSV download, and no search box. Confirm a Punto de Alerta still cannot open it. If the CSV or the history is a new route, extend the existing Swagger test.

**Stop.** None. Do not add a Medio column or a search box to make the CSV "useful".

**Realign.** This screen does not exist as a closed wave. Build it. Do not reuse the last-10 endpoint as the history by raising its take to a large number through `IMerchantStore`.

## Wave 20 — Stories 8.1, 8.2, 8.3

**Outcome.** The referral code, labeled Mi código. The invited commerce pays full price. One free next Pack Mes for the referrer, granted by the Hangfire job wave 3 registered, and not when the referrer is blocked.

**Precondition.** Wave 19 is green. A Pack Mes payment exists from wave 10. The referral-bonus job exists from wave 3 and is still empty.

**Red.** A failing test in `tests/TransfersCheck.Tests/Epic8ReferralTests.cs`.

- Given a commerce, when someone looks for a way to reduce the price, then the only invitation is a referral code. There is no coupon field. The code is labeled "Mi código". The screen does not show a cashback balance.
- When a new commerce pays with that code, then the invited commerce pays the full price. A Pack Evento payment does not grant the bonus.
- Given a referrer inside an active plan, when a new commerce pays its first Pack Mes with that code, then the referrer's next Pack Mes period is free, once, continues without a gap, and is not stored as money.
- And granting that bonus invokes the Hangfire job wave 3 registered. It is not `TransferPollWorker`. A second new commerce paying Pack Mes with the same code does not grant a second free period.
- Given a referrer whose access is blocked, when a new commerce pays its first Pack Mes with that code, then the referrer stays blocked and the free month is not stored.

**Green.** Fill the body of the existing referral job. A Core use case decides the one free period. A small port stores the code and whether the bonus was granted. Do not add a method to `IMerchantStore`. Do not register a second job. Do not add a cron. Do not add a coupon or a cashback balance. `web/` shows "Mi código".

**Verify.** The unit-test command. After the code exists, exercise "Mi código" in the browser and confirm there is no coupon field and no cashback balance.

**Stop.** If the bonus cannot be granted without a second job or a cron expression, stop. Invoke the job wave 3 registered.

**Realign.** Do not create a parallel bonus path outside that job.

## Wave 21 — Story 9.1

**Micro-step, before any production code.** Edit ADR-010 now, and not before. In `_bmad-output/transferscheck/adrs.md`, ADR-010 Decision, after "This record names no hub method.", add this sentence and no others:

> API and web/ share a message contract a test can fail first.

Do not name a SignalR hub method in that sentence or in UI copy. The contract may name a message type. The UI must not say SignalR.

**Outcome.** While the webapp is open, a new transfer appears on the list without waiting for the next poll. The poll stays. A closed webapp still gets the browser notification.

**Precondition.** Wave 20 is green. The ADR-010 sentence above is in the ADR. Wave 8's poll and wave 14's browser notification still exist.

**Red.** A failing contract test that both the API publisher and `web/` consume. Put the API side in `tests/TransfersCheck.Tests/Epic9LiveListTests.cs`. The shared contract is one fixture both sides read. The payload is the new transfer's list fields the list already shows (Fecha, Pagador, Monto, Estado) plus enough identity to put that row on the open list. Reuse the existing `TransferChange` fields if they already carry that payload. Do not invent a second transfer shape.

- Given the webapp is open, when a new transfer is stored, then the shared contract carries that transfer and the list shows it without waiting for the next poll.
- And the poll still runs.
- When the webapp is closed, then the browser notification from wave 14 still fires.
- And no test, fixture, or UI string names a hub method or the word SignalR.

**Green.** A Core use case publishes the contract when a transfer is stored. A small port delivers it to an open client. `web/` subscribes to that contract and inserts the row. Do not add a method to `IMerchantStore`. Do not remove `TransferPollWorker`. Do not remove the push path. Do not put a hub method name in the UI.

**Verify.** The unit-test command, including the contract test failing first and then passing. After the code exists, exercise an open list in the browser: store a transfer and see the row without waiting for the poll interval. Then close the webapp and confirm the browser notification still fires. No Swagger name for a hub.

**Stop.** If the only way to go green is to choose a hub method name, stop. Point at ADR-010 ("Hub method names are not decided here") and story 9.1 ("this story names no hub method"). Do not choose one. A message type on the shared contract is allowed. The UI copy must not name SignalR.

**Realign.** The poll and the notification stay. This wave adds the open-list path beside them.

## Wave 22 — Story 10.1

**Micro-step, before any production code.** Edit ADR-007 now, and not before. In `_bmad-output/transferscheck/adrs.md`, ADR-007 Consequences, in the paragraph that says an implementer does not add a subsystem for the cap, add this sentence and no others:

> A small Core type is allowed for the quota policy: what counts as a consulta, warn at 450, wait at 500, seven days from the link.

That sentence does not create a config-bag subsystem. "No agregar un subsistema" still means no key-value bag and no free-settings screen.

**Definition of a consulta.** Do not invent one. Story 10.1, ADR-007, the weekly-cap row in `brief.md`, `decisions.md`, and `open-decisions.md` close the numbers, the warning, the wall, and the seven-day period from the Mercado Pago link. They do not say which event increments the count. `decisions.md` records that the 23 October wording is "queries and alerts", replacing an earlier "transfers and alerts", so a stored transfer is not the closed definition. Stop before any code that increments a counter, blocks a query, blocks an alert, or shows the web warning, because those need the missing definition. Report that the unit of a consulta is not in the pack. Do not write a human decision. Do not convene party-mode to pick it.

**What may still be built.** The closed arithmetic is a pure Core function. It takes a count that something else already produced, the link instant, and the current instant.

**Outcome of the allowed part.** Warn at 450, wait at 500, seven-day periods from the link instant, same setting for both packs, no overage charge, no recharge pack.

**Precondition.** Wave 21 is green. The ADR-007 sentence above is in the ADR. Wave 2's `WeeklyQueryCap` and `WeeklyQueryWarningGap` exist. Wave 9 stored the link instant.

**Red.** A failing test in `tests/TransfersCheck.Tests/Epic10QuotaTests.cs` against the pure function only.

- Given `WeeklyQueryCap` 500 and `WeeklyQueryWarningGap` 50, when the supplied count is 450, then the result is warn. When 50 remain, that is 450 used.
- When the supplied count is 500, then the result is wait.
- Given the Mercado Pago link instant, when the period is computed, then seven consecutive days run from that instant, the next period starts when the previous one ends, and it is not a calendar week and not Monday.
- And Pack Evento and Pack Mes use the same setting.
- And the function does not charge an overage and does not grant a recharge of 500.
- And the test does not name an event that increments the count.

**Green.** The pure function and the small Core type from the ADR-007 sentence, in `src/TransfersCheck.Core`. No Infrastructure, Api, or `web/` wiring that counts consultas, waits alerts, or shows the message. Do not add a method to `IMerchantStore`. Do not add a subsystem or a settings screen.

**Verify.** The unit-test command for the pure function. No Swagger. No browser. The web message is part of the story and stays unbuilt while the definition is missing.

**Stop.** The consulta stop in the micro-step. The first build's quota behavior on the web is not done until that definition exists. Do not pretend the pure function is the banner or the wait.

**Realign.** Do not turn the weekly cap into `TrialDays`, a price, or a quantity pack.

## Final step — first-build check

Run this only after wave 22's allowed part is green, and after every earlier wave is green. Re-check `dotnet --list-sdks` still satisfies `global.json`. Then run:

`dotnet test tests/TransfersCheck.Tests/TransfersCheck.Tests.csproj`

Exercise the path below in the browser with `scripts/dev-up.ps1` and `npm run e2e` in `web/` for the screens those waves already required. A screenshot is not the pass. The quota web message is on this list only if the consulta stop was cleared by a human decision that already existed in the pack. It was not. Leave that item unchecked and report the stop.

1. The API and `web/` come up as two deployables. Swagger is `/swagger/v1/swagger.json`. The API does not serve the PWA.
2. A tenant read returns only that tenant's rows. Typed settings are cap 500, gap 50, alert-point count 10, email-code lifetime 1 hour, invite lifetime 1 hour, page size 10. `TrialDays` 14 and `PriceAmount` 5999 are not locked.
3. Development `/hangfire` shows the five-day job and the referral job and no others. The poll is `TransferPollWorker`.
4. The door uses the adopted words, one live session, and Salir / Cerrar sesión. The signed-in menu is Transacciones, Configurar, Pagos, and Salir.
5. Configurar enforces Pack Evento (admin only) and Pack Mes (one admin, at most four Puntos de Alerta) and revokes a login.
6. Connect is authorization code with PKCE, a second link of the same Mercado Pago account is refused, the merchant never creates an application, and `mp-authorization-setup.md` has no secrets.
7. The checklist follows real server state. The video URL is unset, so the player is hidden.
8. The last 10 use Fecha, Pagador, Monto, Estado, Desconocido, Llegó, Pendiente, Rechazado. Refresh respects the cooldown. One row per identity. A replica without the lock skips. There is no Sincronizar control.
9. The pay screen shows Pack Evento ARS 1000 and Pack Mes ARS 5999, labels Email and Plan, and the Pack Mes trial starts at the Mercado Pago link for 21 days.
10. The payment link does not move the access date on browser return. No preapproval, cashback, or coupon. The owner account is unset.
11. Five days before the access date, the fake mail port records the notice and the banner shows, except on Pack Evento. Access still works.
12. The web blocks at the access date. Pack Evento blocks at 24 hours.
13. The PWA installs from `web/`. The API does not serve it.
14. An arrived transfer is heard and shown. Pending and rejected stay silent. One tap opens that row. No service-account JSON in the repo. No invented Firebase project values.
15. Configurar shows Habilitar Notificaciones and does not name the mechanism. A Punto de Alerta receives alerts.
16. Sign-in without the email code is refused. The code dies at 1 hour. No length is asserted.
17. The alias is confirmed once as `#alias`. The Pack Mes invite is single-use, lives 1 hour, and is carried with `wa.me`. The password is set from that link.
18. A Punto de Alerta sees the last N rows, default 10, may open one of them, and does not see Historial de Transacciones.
19. The admin sees Historial de Transacciones, the CSV, and the totals by day, by month, and by a date range. No Medio column, no search box, no 10-row cap.
20. Mi código is the only invitation. The invited commerce pays full price. One free next Pack Mes is granted by the existing Hangfire job, and not when the referrer is blocked.
21. An open list shows a new transfer from the shared contract without waiting for the poll. The poll still runs. A closed webapp still gets the browser notification. The UI does not say SignalR and does not name a hub method.
22. The pure Core function warns at 450, waits at 500, and uses seven days from the link. The web message and the live wait stay unchecked while the consulta unit is undefined.

When the tree is clean except for the known out-of-wave dirt, and every item above that is not stopped has been exercised, stop. Report the commit for each wave and every stop that fired.

## Not in this run

Do not implement these. Do not open `later-research.md` to design them. A sentence in an earlier story that points at them does not pull them in.

- Epic 11, stories 11.1, 11.2, and 11.3. Help pages, the help route, and what "ayuda" includes beyond the `EnableCopilotHelp` flag stored in wave 9.
- Epic 12, stories 12.1, 12.2, and 12.3. The owner microsite, plan editing, and the services panel. The owner's Mercado Pago account stays unset.
- The help copilot and Jev. ADR-017 and `help-copilot-technical-spec.md` stay specs. Do not build the chatbot, the index, or the post-deploy ingest.
- The email-code digit length, unless a later human decision is already in the pack. It is not.
- Splitting the existing `IMerchantStore`.
- Choosing or documenting an SDK install. `net10.0` is the target. A machine with only SDK 9 is the execution stop above, not a new design.
- The accumulable recharge of 500 queries. It is not a story.
- A production pipeline, a host, a queue, or a cloud topology.
- Auth0, Google sign-in, coupons, cashback, preapproval, an `AppConfiguration` bag, a generic repository, MediatR, and MonsterASP screens.
