---
title: 'Wave 4: commerce door signs in through Auth0 and issues the session cookie'
type: 'feature'
ticket: '1'
created: '2026-10-24'
status: 'built'
baseline_revision: '4aad267fb612817f7db5c124a0ac1886ea8033a7'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
context:
  - _bmad-output/transferscheck/adrs.md
  - _bmad-output/transferscheck/end-to-end-implementation-runbook.md
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Waves 4 (stories 2.1, 2.3) was built with a local email/password door. ADR-018 makes Auth0 Universal Login the commerce IdP; code, unit tests and `web/e2e` still encode the retired door.

**Approach:** Add an Auth0 authorization-code + PKCE bridge in the API (`/api/auth0/login`, `/api/auth0/callback`) that validates the RS256 id_token, finds or creates the owner account by verified email, and issues the existing HttpOnly session cookie. The web door and signup become Auth0 buttons when the API reports Auth0 enabled. A mock Auth0 in `tools/TransfersCheck.MockServer` is the test double.

## Boundaries & Constraints

**Always:** Auth0 Authority, client id and client secret come from configuration (user-secrets/environment), never committed. `TenantId` comes only from the server session. Cookie stays HttpOnly, Secure, `SameSite=None`. A new login replaces the previous live session. Mercado Pago OAuth is untouched.

**Never:** Auth0 on Punto de Alerta (`/api/sessions` with commerce+user, `/api/invites`) or the owner microsite. No new `IMerchantStore` method. Do not remove the local email-code path (wave 16, deferred). No commits, no push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Signup via Auth0 | verified email never seen | merchant + owner account created, session cookie, redirect to UI | none |
| Login via Auth0 | verified email of existing owner | session cookie; previous session ends | none |
| Unverified email | `email_verified=false` | no session | redirect `UI/?auth0_error=email_unverified` |
| Tampered state or missing state cookie | wrong `state` | no session | redirect `auth0_error=invalid_state` |
| Email of a Punto de Alerta or revoked account | non-owner or revoked | no session | redirect `auth0_error=not_allowed` |
| Auth0 enabled, local owner door | `POST /api/accounts` or owner `POST /api/sessions` | 410 `use_auth0` | n/a |
| Auth0 disabled | no Authority configured | local door unchanged; `/api/auth0/login` 404 | n/a |
| Punto de Alerta sign-in while Auth0 enabled | commerce+user+password | still works | n/a |

</frozen-after-approval>

## Code Map

- `src/TransfersCheck.Core/Options.cs` -- add `Auth0Options` (Authority, ClientId, ClientSecret, RedirectUri, `Enabled`).
- `src/TransfersCheck.Core/Ports.cs` -- add `IIdentityProvider` (`BuildAuthorizationUrl`, `ExchangeAsync` -> `IdentityClaims`); mirrors `IMerchantAuthorizer`.
- `src/TransfersCheck.Infrastructure/MercadoPagoAuthorizer.cs` -- pattern for HttpClient exchange; `DependencyInjection.cs` -- register provider; `.csproj` -- add `Microsoft.IdentityModel.JsonWebTokens`.
- `src/TransfersCheck.Api/Accounts/AccountEndpoints.cs` -- `SignUp`/`SignIn` owner path guarded by Auth0; make `Issue` internal for reuse.
- `src/TransfersCheck.Api/Connections/ConnectionEndpoints.cs` -- redirect-to-UI pattern; `Pkce.cs` for verifier/challenge.
- `src/TransfersCheck.Api/Program.cs` -- bind `Auth0Options`, map new endpoints.
- `tools/TransfersCheck.MockServer/MockEndpoints.cs` -- add `/auth0/authorize` (form, or `login_hint` shortcut), `/auth0/oauth/token`, `/auth0/.well-known/jwks.json`; RS256 id_token.
- `tests/TransfersCheck.Tests/Epic2LoginTests.cs`, `TestAuth.cs`, `Epic1PlatformTests.cs` (factory) -- Auth0 tests.
- `web/src/App.tsx` -- `Enter` and `CreateAccount`; `web/e2e/*.spec.ts`, `helpers.ts`, `start-api.ps1` -- Auth0 e2e.

## Tasks & Acceptance

**Execution:**
- [ ] Core/Infrastructure -- options, port, `Auth0IdentityProvider` (JWKS cache, issuer, audience, nonce, expiry checks)
- [ ] Api -- `Auth0Endpoints` (state cookie via `ITokenProtector`, config, login, callback); guard local owner door
- [ ] MockServer -- Auth0 double
- [ ] Tests -- replace `Sign_up_sets_a_cross_site_cookie...` with Auth0 form; add replace-session, sign-out, matrix rows
- [ ] Web + e2e -- Auth0 door; helper `signUpWithAuth0`; drop "no Auth0/Google" asserts; update specs that typed the local form; start-api env

**Acceptance Criteria:**
- Given Auth0 is configured, when a commerce admin completes Auth0, then the cookie is HttpOnly, Secure, `SameSite=None` and a client-supplied tenant is ignored.
- Given the login and signup screens, when they render, then they show the wordmark, no Menú, and Auth0 buttons; Mercado Pago is not the door.
- Given a live session, when the person uses Salir then Cerrar sesión, then the screen is the login screen.

## Implementation Notes

Implemented directly from the plan (no separate coding subagent: this run was itself a nested subagent). Files: Core (Options, Ports), Infrastructure (Auth0IdentityProvider, DI, csproj), Api (Auth0Endpoints, AccountEndpoints guard, Program startup check), MockServer (Auth0Mock), tests (Epic2LoginTests, TestAuth, Epic1PlatformTests factory), web (App.tsx, api.ts, e2e specs, helpers, start-api.ps1). Verification: dotnet test 46 passed (baseline 41); web build ok; npm run e2e 15 passed in repeated runs. The Auth0 door is on only when Auth0:Authority is set; without it the local door stays (dev without tenant) until wave 16. Mock fix: the RSA key is locked while signing because parallel e2e logins produced bad signatures.

## Plan Change Log

## Review Triage Log

Quick lens, iteration 1 (8 findings):

| # | Verdict | Route | Evidence |
|---|---------|-------|----------|
| 1 | medium | patch | Callback caught only InvalidOperationException/HttpRequestException; a timeout (TaskCanceledException), malformed JWKS or Authority would 500. Now caught broadly unless the request is cancelled; test `exchange_failed` added. The claim that a lock is held across the JWKS fetch is false: the lock covers only the cache read and write. |
| 2 | medium | patch | Enabled on Authority alone closes the local door with a half-filled config. Startup now fails when Authority is set without ClientId, ClientSecret and RedirectUri. |
| 3 | maybe-false | defer | Email-only linking adopts a pre-existing local owner. Production never shipped the local door; settle by checking production owner accounts. |
| 4 | false | rejected | Runbook wave 4 and wave 16 retire `email-code.spec.ts`; the local code door keeps its unit tests (factory without Auth0). Logged as a note in deferred work. |
| 5 | low | patch | Tenant-ignored assertion was true by construction. Test now names an existing second tenant and asserts the session is the new account's own. |
| 6 | low | patch | Missing exchange-failure test added. Revoked owner is unreachable (`Revoke` refuses owners), so `not_allowed` is covered by the non-owner case. |
| 7 | medium | defer | Federated logout is unspecified in ADR-018 (intent gap). Deferred with the decision stated; does not block wave 4. |
| 8 | low | patch | Restored the csproj BOM and trailing newlines. |

## Design Notes

State travels in a short-lived protected cookie (`tc_auth0`, Lax, path `/api/auth0`) holding state, PKCE verifier and nonce, so no tenant-less DB row is needed. Mock issuer is derived from the request host so Development and tests share one code path. Mock rule: email starting `unverified.` gets `email_verified=false`.

## Verification

**Commands:**
- `dotnet test TransfersCheck.slnx` -- expected: all pass (baseline 41)
- `npm run build` and `npm run e2e` in `web/` -- expected: pass
