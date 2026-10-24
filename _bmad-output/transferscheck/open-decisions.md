# Open decisions

23 October 2026, night. The weekly-cap cutoff and the old-UI salvage are closed. They are in `adrs.md`, `decisions.md`, and `party-old-ui-2026-10-23.md`. Nothing in this file is a build queue. Do not copy a line from `later-research.md` into an ADR or into a story acceptance check.

## Still unknown

What is still unknown is `later-research.md`. That file is pending research, resume later. It is not a build queue.

- Owner microsite, including superadmin login, plan editing, the services panel, the helpdesk, and the log/PR agent. Hosts are not designed there.
- Help copilot and Jev. Provider names, model thresholds, and the index name are still unset. The spec is `help-copilot-technical-spec.md`. Pack Mes `EnableCopilotHelp` and the Pack Evento static menu stay decided. Implementation waits.
- Production pipeline and the post-deploy help ingest. There is no GitHub Actions pipeline today.

## Closed 24 October 2026 (first-build)

- Live-list contract: API and web/ share a message type a test can fail first; UI copy must not say SignalR. ADR-010 / story 9.1.
- A **consulta** is an on-demand transfer refresh/query (not opening the menu or settings). Cap 500, warn at 450, seven days from the Mercado Pago link. ADR-007 / story 10.1.

## Closed 24 October 2026 (Auth0 course correction)

- Commerce signup/login identify with Auth0 Universal Login: SSO **or** Database email/password. ADR-018 / story 2.1.
- Punto de Alerta stays usuario/clave (not Auth0 SSO). ADR-013 / stories 7.3–7.4.
- Owner microsite stays out of Auth0. ADR-014.
- Mercado Pago OAuth stays collection-account linking, not the IdP. ADR-004.
- After Auth0, API still issues the local session cookie. ADR-002 / ADR-008.
- Local product-issued 6-digit email code on every commerce sign-in is withdrawn for the Auth0 path. Story 7.1. (Earlier same-day lock of "6 numeric digits" applied to that withdrawn local door.)

### Defaults recorded (reversible)

- Social SSO default provider: **Google** via Auth0. More social providers may be enabled in the Auth0 tenant without a new ADR.
- Auth0 Database connection for usuario/contraseña (not a separate custom password store for commerce admin).
- Bridge: Auth0 IdP → API HttpOnly Secure `SameSite=None` cookie.

### Still open (do not invent in code without a decision)

- Whether Auth0 MFA is required for every commerce admin, optional, or off by default.
- Exact Auth0 tenant / application names and callback URLs for production (typed config; values not in this pack).
- Whether Universal Login is hosted redirect only or also embedded widget in `web/`.

## Not this file

- The weekly-cap cutoff instant. Closed 23 Oct 2026, night. ADR-007.
- The old-UI salvage. Adopted by the human on 23 Oct 2026. `party-old-ui-2026-10-23.md`.
- The accumulable recharge of 500 queries. Not in scope. ADR-007. Not a story.
