# Sprint Change Proposal — Commerce Auth0 course correction

Date: 24 October 2026  
Trigger: Human product/architecture correction during first-build execution  
Scope classification: **Moderate** (planning artifacts amended now; implementation realign of waves 4 and 16 next)  
Path forward: **Direct Adjustment** (amend ADRs, stories, runbook; no epic rollback; no MVP shrink)

## 1. Issue Summary

The pack had locked commerce signup/login as a local email/password door with no Auth0 and no Google, plus a local email code on every commerce-admin sign-in. The human corrected that decision: commerce must identify with Auth0, simplify to SSO or usuario/contraseña, keep Punto de Alerta on usuario/clave, and keep Mercado Pago OAuth as payment-account linking only.

Evidence: explicit human course-correction request; contradictory locks in ADR-002, ADR-013, story 2.1, story 7.1, runbook waves 4 and 16, `first-build-prompt.md`, and the 23 Oct night leave in `decisions.md`.

## 2. Impact Analysis

### Epic impact

- Epic 2 (stories 2.1, 2.3): commerce door becomes Auth0 Universal Login; session cookie after identity stays.
- Epic 7 (story 7.1): local email-code door withdrawn; Auth0 owns verification/MFA if any. Stories 7.3–7.4: exception made explicit (usuario/clave, not Auth0 SSO).
- Epic 12 (story 12.1): unchanged — owner microsite stays out of Auth0.
- Epic 3 Mercado Pago connect: unchanged as IdP; still collection-account OAuth only.

### Artifact conflicts resolved in this pass

| Artifact | Change |
| --- | --- |
| `adrs.md` | ADR-002, ADR-007, ADR-008, ADR-012, ADR-013, ADR-014 amended; **ADR-018** added |
| `decisions.md` | New 24 Oct Auth0 entry; historical night leaves struck where they conflicted |
| `epics-and-stories.md` | Stories 1.4 note, 2.1, 7.1, 7.3, 7.4 |
| `brief.md` | Commerce sign-in and scope bullets |
| `parking-lot.md` / `parking-lot-waves.md` | Commerce Auth0 in scope; PL-12 unparked for commerce |
| `end-to-end-implementation-runbook.md` | Waves 4 and 16 realign; checklist item 16; "Not in this run" |
| `first-build-prompt.md` | Auth0 in for commerce; wave 4 and 16 text |
| `open-decisions.md` | Closed Auth0 model + recorded defaults + remaining opens |
| `party-old-ui-2026-10-23.md` | Supersession note on commerce Auth0 Dejar |
| `saas-reconciliation.md` | Supersession note on Auth0 "Leave parked" row |
| `deploy/README.md` | Auth0 commerce config note |

### Technical impact (not implemented in this pass)

- Realign API + `web/` to Auth0 IdP → local session cookie bridge.
- Retire e2e/unit asserts that forbid Auth0/Google and that require a local Código field.
- Auth0 secrets stay in user-secrets/environment.

## 3. Recommended Approach

**Option 1 — Direct Adjustment (selected).** Effort: Medium. Risk: Medium (Auth0 tenant wiring). Keeps epics; amends stories and ADRs; realigns waves 4 and 16.

Option 2 (rollback local auth waves) rejected: cookie/session work still valid under the bridge.

Option 3 (MVP shrink) rejected: Auth0 simplifies signup; it does not cut commerce value.

## 4. Detailed change proposals

Applied in-repo (batch mode per coordinator). Summary of new acceptance shape:

**Story 2.1** — Commerce Auth0 Universal Login (SSO or Database password) → API session cookie; MP OAuth not this door; Punto de Alerta excluded.

**Story 7.1** — No local Código on every commerce sign-in; Auth0 owns extra verification/MFA.

**ADR-018** — Source of truth for the Auth0 model and defaults (Google social default; Database connection; bridge cookie).

## 5. Implementation handoff

- **Planning/docs:** Done in this pass (no commit unless the human asks).
- **Developer agent:** Realign waves 4 and 16 against ADR-018; do not Auth0 the owner microsite or Punto de Alerta; do not commit Auth0 secrets.
- **Success criteria:** Story 2.1 and 7.1 acceptance match ADR-018; runbook waves 4/16 green; e2e no longer asserts "no Auth0".

## Checklist (correct-course)

- [x] 1.1–1.3 Trigger and evidence understood
- [x] 2.1–2.5 Epic impact assessed (Epic 2 and 7 adjust; 12 unchanged)
- [x] 3.1–3.4 Artifacts updated (no separate UX spec file in pack)
- [x] 4.1–4.4 Direct Adjustment selected
- [x] 5.1–5.5 Proposal written
- [x] 6.1–6.2 Consistency review of main decision artifacts
- [x] 6.3 Explicit human approval of implementation realign (given 24 Oct 2026; code realign starts with wave 4, wave 16 deferred)
- [x] 6.4 PL-12 status updated in `parking-lot-waves.md`
- [x] 6.5 Handoff: Developer realigns waves 4 and 16

## Defaults recorded (open until human overrides)

1. Auth0 Universal Login + Database connection + Social (Google default).
2. Auth0 IdP → API local session cookie (not Auth0 replacing `TenantId`).
3. MFA optional via Auth0 config (not a product-issued 6-digit store).
4. Hosted vs embedded Universal Login left open in `open-decisions.md`.
