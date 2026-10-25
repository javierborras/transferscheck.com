- source_plan: none
  summary: Realign wave 16 (story 7.1) so commerce sign-in has no product-issued Código; Auth0 owns extra verification (ticket 1.2 in initiative-transferscheck-auth0-realign).
  evidence: Done on develop — Auth0 path returns `use_auth0` (410); UI hides Código for Auth0 admins; residual local Código remains only when Auth0:Authority is empty (factory / offline door).
  status: done

- source_plan: `_bmad-output/initiative-transferscheck-auth0-realign/tickets/epic-commerce-auth0-realign/tickets/story-wave-4-commerce-door-signs-in-through-auth0-and-issues-the-s-plan.md`
  summary: Link commerce owners by Auth0 `sub` (stored on the account) and decide how to treat owner accounts that already exist from the local door.
  evidence: Done — `LoginAccount.Auth0Subject` stored; callback finds by sub then email and links subject on first Auth0 use. Production never shipped the local door.
  status: done

- source_plan: `_bmad-output/initiative-transferscheck-auth0-realign/tickets/epic-commerce-auth0-realign/tickets/story-wave-4-commerce-door-signs-in-through-auth0-and-issues-the-s-plan.md`
  summary: Decide federated logout so Cerrar sesión also ends the Auth0 Universal Login session (`/v2/logout?returnTo=`) or the next login forces `prompt=login`.
  evidence: Done — DELETE `/api/sessions` returns `federatedLogoutUrl` for Auth0 commerce admins; web navigates there after clearing the API cookie. Add `Auth0:LogoutReturnUri` (or `Ui:Origin`) to Allowed Logout URLs in the Auth0 tenant.
  status: done

- source_plan: `_bmad-output/initiative-transferscheck-auth0-realign/tickets/epic-commerce-auth0-realign/tickets/story-wave-4-commerce-door-signs-in-through-auth0-and-issues-the-s-plan.md`
  summary: Browser coverage of the local email-code door is gone from e2e (deleted `email-code.spec.ts`); wave 16 removes that door and its unit tests.
  evidence: `start-api.ps1` turns Auth0 on, which closes the local owner door in e2e. Unit tests (factory without Auth0) still cover the code door for the offline path.
  status: accepted-residual
