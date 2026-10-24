# First-build loop status

Updated: 24 October 2026.

## Completed green (pushed to `origin/mvp/transferscheck`)

| Wave | Commit | Notes |
|------|--------|-------|
| 2 | `bdf3b83` | Typed commercial caps; runbook + Playwright harness |
| 3 | `5515018` | Hangfire empty jobs `five-day-notice` / `referral-bonus` via `Cron.Never()` |
| 4 | `27aaf0f` | Adopted door copy, menu, sign-out |
| 5 | `dfd482e` | Pack Evento / Pack Mes login caps (`LoginAccountCap`) |
| 6 | `d2bc703` | MP operator checklist without secrets / withdrawn prices |
| 7 | `774d88c` / `bf9fa9c` | Checklist on real connection state; connect badges |
| 8 | `aff151d` | Last-10 list columns + Desconocido; no Sincronizar |
| 9 | `dd3a338` | Plan table Pack Evento/Mes; 21-day Pack Mes trial from link |
| 10 | `e3067e8` | Prepaid payment link; return does not move access; owner token unset |
| 11 | `5f39fd3` | Five-day notice via Hangfire job + banner; NullMailSender unbound |
| 12 | `87b8427` | Access-date web block; Pack Evento 24h from link |
| 13 | `9416490` | Installable PWA from web/; API does not serve it |
| 14–15 | `4bd68c2` | FCM arrived push + Habilitar Notificaciones (no mechanism name) |

## Next

**STOP before wave 16** — email-code digit length is an open decision (ADR-013 / `open-decisions.md`). Do not invent the length.

## Verify

```text
dotnet test tests/TransfersCheck.Tests/TransfersCheck.Tests.csproj
cd web; npm run e2e
```

## Open decisions (do not invent)

- Wave 16 / story 7.1: email-code digit length
- Wave 21 / story 9.1: SignalR hub method name (shared message type OK; UI must not say SignalR)
- Wave 22 / story 10.1: what counts as a "consulta" if not in live docs
