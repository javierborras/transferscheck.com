
- source_plan: none
  summary: Realign wave 16 (story 7.1) so commerce sign-in has no product-issued Código; Auth0 owns extra verification (ticket 1.2 in initiative-transferscheck-auth0-realign).
  evidence: Split chosen 24 Oct 2026 so wave 4 (stories 2.1, 2.3) ships first; wave 16 depends on it (runbook wave 16 precondition).

- source_plan: `_bmad-output/initiative-transferscheck-auth0-realign/tickets/epic-commerce-auth0-realign/tickets/story-wave-4-commerce-door-signs-in-through-auth0-and-issues-the-s-plan.md`
  summary: Link commerce owners by Auth0 `sub` (stored on the account) and decide how to treat owner accounts that already exist from the local door.
  evidence: Review finding (maybe-false, would be medium if a pre-Auth0 local account exists in production): `Auth0Endpoints.Callback` adopts an existing owner by verified email only, so an unverified local signup of another person's email is handed to the real owner. Production never shipped the local door, so it settles by checking the production DB for owner accounts without Auth0.

- source_plan: `_bmad-output/initiative-transferscheck-auth0-realign/tickets/epic-commerce-auth0-realign/tickets/story-wave-4-commerce-door-signs-in-through-auth0-and-issues-the-s-plan.md`
  summary: Decide federated logout so Cerrar sesión also ends the Auth0 Universal Login session (`/v2/logout?returnTo=`) or the next login forces `prompt=login`.
  evidence: Review finding (real, intent gap): Salir only ends the API session, so on a shared counter device the Auth0 button signs the same person back in silently with a real tenant. The mock tenant has no SSO session and hides it. Needs a product decision plus an allowed-logout-URL entry in the Auth0 tenant.

- source_plan: `_bmad-output/initiative-transferscheck-auth0-realign/tickets/epic-commerce-auth0-realign/tickets/story-wave-4-commerce-door-signs-in-through-auth0-and-issues-the-s-plan.md`
  summary: Browser coverage of the local email-code door is gone from e2e (deleted `email-code.spec.ts`); wave 16 removes that door and its unit tests.
  evidence: `start-api.ps1` now turns Auth0 on, which closes the local owner door in e2e. Unit tests (factory without Auth0) still cover the code door until wave 16.
