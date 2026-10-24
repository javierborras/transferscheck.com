# Parking lot

Locked 21 October 2026. Amended 22 October 2026 during the parking-lot review. Amended 23 October 2026 where a sentence below was no longer true, including the night close of the week start. Items now in scope point at an ADR. Discarded items stay discarded. What is still unknown is `later-research.md`, indexed from `open-decisions.md`. That file is not a backlog.

## Now in scope

- **Pack Evento, 24 hours.** ADR-009 and ADR-004. The old "no day pass" line is withdrawn.
- **Plan table and two starting offers.** ADR-009. The owner edits plans on the microsite (ADR-014). The plan table is in the first bmad-build. The microsite editor is not (`later-research.md`).
- **Referral code.** ADR-015. One free next Pack Mes, once, and only for an active referrer.
- **Weekly usage cap of 500 queries, warning gap 50.** ADR-007. The commerce is warned at 450. At 500, new queries and alerts wait. The weekly-cap cutoff instant is the moment Mercado Pago is linked. From that instant, seven consecutive days run. The next period starts when the previous one ends. The 21-day trial uses that same start. The recharge is not in scope.
- **SignalR beside the poll and the browser notification.** ADR-010. No hub method is named.
- **Hangfire runs the five-day notice and the referral bonus.** ADR-011. No other Hangfire job. The poll stays `TransferPollWorker`.
- **Owner services panel.** ADR-012. Not a key-value bag. Not the first bmad-build (`later-research.md`). The mail provider value stays unset.
- **Commerce admin, Punto de Alerta, and the alias.** ADR-008 and ADR-013.
- **Commerce Auth0 Universal Login (SSO or Database email/password).** ADR-018. Punto de Alerta stays usuario/clave. Mercado Pago OAuth stays collection-account linking, not the IdP. Owner microsite stays out of Auth0 (ADR-014).
- **Owner microsite sign-in.** ADR-014. Not the first bmad-build (`later-research.md`). Do not design a host.
- **Help pages in `web/help/`, plus the plan flag and the two help modes.** ADR-016 and ADR-017. Not a help CMS. Not a chatbot on the connect screen. Pack Mes `EnableCopilotHelp` and the Pack Evento static menu stay decided. Implementation of the copilot, Jev, and the production pipeline is not the first bmad-build (`later-research.md`).
- **Brand manual as a design reference.** `Docs/ManualdeMarcayUI.html`. Not a product module and not an approval gate.

## Still do not build

### Product

- **Prepaid quantity packs of 50, 100, 500, and 1000+.**
- **A credit ledger.** Access is a date.
- **Cashback and discount coupons.**
- **A store product.** No tills, no cajas, no cashier role, no per-seat price, and no permission matrix. The second role is Punto de Alerta (ADR-013).
- **A second payment provider already connected.** `ITransferSource` stays. The only implementation is `mercadopago`. Do not list providers. Do not build a second one.
- **Native apps and app-store clients.** The client is the commerce webapp. The phone is the Pack Evento situation.

### Support

- **A provider leader, a ranking ladder, or a second help service.** ADR-017 keeps an explicit choice among two or three adapters inside the existing API. Do not add the leader. Provider names, model thresholds, and the index name stay unset in `later-research.md`. Do not design them. The help corpus itself is in scope (ADR-016). Implementation of the copilot waits.
- **A help CMS or an FAQ publisher.** Help is the folder `web/help/`.
- **A connect-screen chatbot.** The human rejected that assistant on 21 Oct 2026. The first run is still a checklist.

### Commercial extras that stay out

- **Auth0, Google, Cloudflare Access, and any other third party for the owner microsite login.** Commerce Auth0 (ADR-018) does not open that microsite. Mercado Pago OAuth does not open that microsite.
- **A business-config screen on the commerce webapp, including Backend Config / Pruebas.** Plan editing and the services panel are on the owner microsite.
- **The owner marking a payment by hand, or watching a bank account.**
- **Preapproval, automatic debit, and an extra debit layer.**

### Later, not an epic

- **An accumulable recharge of 500 queries.** The human may evaluate it later from real application telemetry. They believe 500 will be enough. Not a story.
- **The microsite helpdesk.** Pending research. Not the first bmad-build (`later-research.md`).
- **A Cursor agent that reads logs, files bugs, and opens a GitHub pull request for the owner to approve.** Intent only. Not specified. Parked with the helpdesk. Not the first bmad-build (`later-research.md`).

The owner microsite, the help copilot, Jev, and the production pipeline stay product decisions and are not the first bmad-build. `later-research.md`. There is no GitHub Actions pipeline today. Do not invent one.

### This pack

- **Process skills and gates.** Verification remains tests in `tests/TransfersCheck.Tests`, Swagger, and the browser.

MediatR, a generic repository, and a key-value configuration bag stay out. They are not queued work.
