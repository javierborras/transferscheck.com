# Later research

Status: pending research, resume later. Not a build queue.

## First build

The first bmad-build is the closed product already aimed at the new repo, and nothing else: plans (Pack Evento and Pack Mes, and the plan table), the 21-day trial from the Mercado Pago link, the prepaid payment link, the five-day Hangfire notice, the referral bonus on Hangfire, the Punto de Alerta invite, the weekly cap of 500 with a warning at 450 and the seven-day periods from the link instant, SignalR while the page is open, and the adopted UI labels.

The owner microsite, the help copilot, Jev, and the production pipeline are not in that build.

## Owner microsite

The first build left this out. Sign-in, plan editing, and the services panel shipped later. On 25 Oct 2026 the dashboard host shipped with them: Inicio, Clientes, Facturaciones, Planes, and Servicios (ADR-014, story 12.4).

On 27 Oct 2026 the helpdesk and the incident agent left this file. Helpdesk is ADR-031 and story 12.4. The agent is ADR-025 through ADR-030 and ADR-032. Commercial data and a money balance stay out. The concrete mail provider value stays unset.

## Help copilot and Jev

Pending research. Not in the first build. Do not design provider names, model thresholds, or the index name. They are still unset.

The spec is `help-copilot-technical-spec.md`. ADR-017. Pack Mes `EnableCopilotHelp` and the Pack Evento static menu stay product decisions. Implementation waits.

## Production pipeline and the post-deploy help ingest

Pending research. Not in the first build. There is no GitHub Actions pipeline today. Do not invent one.

ADR-016 keeps the hook that a separate refresh runs after the UI is in production, and that a failed refresh leaves production up. This file does not define that pipeline.

## Out of the first build

The owner microsite, the help copilot, Jev, and the production pipeline, including the post-deploy help ingest, are explicitly out of the first build.
