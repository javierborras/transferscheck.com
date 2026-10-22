# Parking lot

Locked 21 October 2026. Explicitly out of the MVP. Do not build these in the first version. Do not treat this list as a backlog to pull forward.

## Product

- **24-hour day pass.** Access is the 14-day trial and the 30-day prepaid period.
- **Prepaid packs of 50, 100, 500, and more than 1000.** The offer is one price, ARS 5999 every 30 days.
- **Merchant credit ledger (cuenta corriente).** There is no ledger.
- **Store product.** Internal point-of-sale admin, 1000 transactions a week, five points of sale, a role matrix, and a per-seat price. transferscheck is five logins on one merchant, not that product.
- **A second payment provider already connected.** `ITransferSource` exists. The only implementation is `mercadopago`. The second implementation stays unbuilt.
- **Native apps.** The client is the React PWA. React Native and other native apps stay out. iPhone push depends on Add to Home Screen.
- **The 500-transaction weekly cap.** There is no usage cap.

## Support and publishing

- **LLM support, including System One.**
- **Auto-published help, a help publisher, and an FAQ CMS.** The onboarding video is one typed URL, `Onboarding:MercadoPagoVideoUrl`, not a CMS entry.

## Onboarding

- **Semantic onboarding assistant.** Jev, RAG, multi-provider chat, and a tool-calling bot stay out. The human rejected that assistant on 21 Oct 2026.
- The static checklist and the video are in the MVP. They are specified in `brief.md` and `mp-authorization-setup.md`. They are not items to pull from this list.

## Commercial extras

- **Coupons, referrals, and cashback.**
- **Auth0.** Login is the local JWT and the HttpOnly Secure cookie.
- **Business-config admin UI.** Intervals, price, and secrets stay in typed configuration.

## This pack

- **Brand manual.** UI copy for this MVP is the UI rules paragraph and the setup checklist in `brief.md`. A separate brand manual stays out.
- **Skills and process gates.** TransfersCheck.Tests remains the test project named in ADR-002. Skill files and extra gate documents are out of this pack.

Architecture exclusions that are already decided, and are not queued work, live in `adrs.md`: MediatR, a generic repository, Hangfire, SignalR, a key-value configuration bag, plan tables, and preapproval.
