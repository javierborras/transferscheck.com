# Help corpus (ADR-016)

Markdown pages in this folder are the index corpus. `Onboarding:MercadoPagoVideoUrl` is not part of this corpus.

## Refresh

After a production UI deploy succeeds, run a **separate** ingest so a failed refresh leaves the site up and keeps the previous index:

1. Owner session: `POST /api/owner/help/refresh` (reads this folder or `HelpCopilot:CorpusPath`).
2. Or the scaffolded workflow [`.github/workflows/help-ingest.yml`](../../.github/workflows/help-ingest.yml) (`workflow_dispatch`).

Do not couple ingest failure to UI rollback.
