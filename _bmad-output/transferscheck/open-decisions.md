# Open decisions

23 October 2026, night. The weekly-cap cutoff and the old-UI salvage are closed. They are in `adrs.md`, `decisions.md`, and `party-old-ui-2026-10-23.md`. Nothing in this file is a build queue. Do not copy a line from `later-research.md` into an ADR or into a story acceptance check.

## Still unknown

What is still unknown is `later-research.md`. That file is pending research, resume later. It is not a build queue.

- Owner microsite, including superadmin login, plan editing, the services panel, the helpdesk, and the log/PR agent. Hosts are not designed there.
- Help copilot and Jev. Provider names, model thresholds, and the index name are still unset. The spec is `help-copilot-technical-spec.md`. Pack Mes `EnableCopilotHelp` and the Pack Evento static menu stay decided. Implementation waits.
- Production pipeline and the post-deploy help ingest. There is no GitHub Actions pipeline today.

## Closed 24 October 2026 (first-build)

- Email sign-in code length: **6 numeric digits** (example 482913), lifetime 1 hour. ADR-013 / story 7.1.
- Live-list contract: API and web/ share a message type a test can fail first; UI copy must not say SignalR. ADR-010 / story 9.1.
- A **consulta** is an on-demand transfer refresh/query (not opening the menu or settings). Cap 500, warn at 450, seven days from the Mercado Pago link. ADR-007 / story 10.1.

## Not this file

- The weekly-cap cutoff instant. Closed 23 Oct 2026, night. ADR-007.
- The old-UI salvage. Adopted by the human on 23 Oct 2026. `party-old-ui-2026-10-23.md`.
- The accumulable recharge of 500 queries. Not in scope. ADR-007. Not a story.
