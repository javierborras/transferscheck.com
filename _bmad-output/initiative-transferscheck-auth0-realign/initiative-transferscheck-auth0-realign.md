---
id:
type: initiative
title: "Commerce admins identify with Auth0 and the API keeps issuing the local session cookie"
covers: []
sources:
  - _bmad-output/transferscheck/adrs.md
  - _bmad-output/transferscheck/sprint-change-proposal-2026-10-24-auth0-commerce.md
  - _bmad-output/transferscheck/end-to-end-implementation-runbook.md
companions: []
after: []
risk: medium
---

# Commerce Auth0 realign

## Description

ADR-018 (24 Oct 2026) withdrew the local-only commerce door. Waves 4 (stories 2.1, 2.3) and 16 (story 7.1) were built under the retired rule and must be realigned in code: Auth0 Universal Login is the commerce IdP, and the API still issues the HttpOnly local session cookie.

## Outcome

A commerce admin signs up and signs in through Auth0 (SSO or Database password) and lands on one live API session; no TransfersCheck-owned Código field exists on the commerce door.

## Requirements

R1 (adrs ADR-018): Commerce signup and login identify with Auth0 Universal Login; the API issues the session cookie afterwards and `TenantId` comes only from the server session.
R2 (adrs ADR-018): No local email-code field is required on commerce sign-in (story 7.1 realign).
R3 (adrs ADR-018): Punto de Alerta (usuario/clave) and the owner microsite stay out of Auth0; Mercado Pago OAuth is not the login.

## Constraints

- Auth0 domain, client id and client secret never enter the repo; they live in user-secrets or environment (runbook waves 4 and 16, Stop).
- No new method on `IMerchantStore`; the seam is Auth0 to API session.

## Done when

- Commerce door and e2e assert Auth0 and no longer assert "no Auth0 / no Google".
- Unit tests and `npm run e2e` for the door pass.
- Wave 16 no longer requires a local Código.

## Boundaries

- Must not change: Punto de Alerta invite path (waves 17-18); owner microsite login; Mercado Pago OAuth linking.

## References

- adrs — _bmad-output/transferscheck/adrs.md, ADR-018
- runbook — _bmad-output/transferscheck/end-to-end-implementation-runbook.md, waves 4 and 16
- sprint change proposal — _bmad-output/transferscheck/sprint-change-proposal-2026-10-24-auth0-commerce.md

## Notes

- Decision: 2026-10-24, the user chose Split; wave 4 (stories 2.1, 2.3) goes first, wave 16 (story 7.1) is deferred.
- Decision: 2026-10-24, the user gave explicit human approval (checklist 6.3) to realign in code.
