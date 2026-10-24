# Deploy (local secrets)

Production hosting for the **API** is MonsterASP.net WebDeploy (`site56119`). Credentials and connection strings are **not** tracked in git. The UI (`web/`) deploys separately to Cloudflare Workers.

## Where secrets live

| Item | Local location (gitignored) |
| --- | --- |
| WebDeploy publish profile | `src/TransfersCheck.Api/Properties/PublishProfiles/site56119-WebDeploy.pubxml` (`*.pubxml`) |
| WebDeploy publishSettings | `deploy/local/WebDeploy.Production.transferscheck.com-WebDeploy.publishSettings` (`deploy/local/`, `*.publishsettings`) |
| Full production notes | `deploy/local/production-settings.local.md` |
| Optional Production overlay | `src/TransfersCheck.Api/appsettings.Production.local.json` (`appsettings.*.local.json`) |
| Dev / machine secrets | `dotnet user-secrets` on `TransfersCheck.Api` (`UserSecretsId`: `transferscheck-api`) |

**Do not** leave Control Panel downloads under tracked `deploy/` (for example `deploy/*-WebDeploy*.publishSettings`). Move them into `deploy/local/` with a name without `(2)` / download suffixes. `*.publishsettings` is gitignored, but the local folder keeps secrets out of the tracked tree.

## Expected keys (no values in this tracked file)

- WebDeploy: site id / MSDeploy host / username / password / IIS app path
- SQL: server, database, user id, password → `ConnectionStrings:TransfersCheck`
- Mercado Pago: `MercadoPago:ClientId`, `MercadoPago:ClientSecret`, redirect URI
- SMTP (when mail is wired): host, port, SSL, from address (optional Option A password)

## Manual publish (recommended for production cutover)

From Visual Studio: Import `deploy/local/WebDeploy.Production.transferscheck.com-WebDeploy.publishSettings` into `TransfersCheck.Api`, or use the local `site56119-WebDeploy` profile.

From CLI (uses the gitignored `.pubxml`):

```powershell
dotnet publish src/TransfersCheck.Api/TransfersCheck.Api.csproj -c Release /p:PublishProfile=site56119-WebDeploy
```

Docs: [Visual Studio Web Deploy](https://help.monsterasp.net/websites/deploy/visual-studio), [command-line MSDeploy](https://help.monsterasp.net/websites/deploy/command-line).

## MonsterASP Git deploy — fit for this monorepo

Hub: [Git deploy](https://help.monsterasp.net/websites/git-deploy).

| Approach | Verdict | Why for TransfersCheck |
| --- | --- | --- |
| Control Panel → [Deploy from a GitHub repository](https://help.monsterasp.net/websites/git-deploy/github-repository) (`Build .NET app`) | **Conditional** | Docs allow choosing one `.csproj` in a multi-project repo — select `src/TransfersCheck.Api/TransfersCheck.Api.csproj`. Still risky: server-side build must support **.NET 10** (`net10.0`); the monorepo also has Cloudflare `web/` (must never use Node/Static types against the repo root); production secrets must stay out of git (use Protected files / host env). Fine as a manual pull/build experiment, not the default cutover path. |
| [GitHub Actions + WebDeploy](https://help.monsterasp.net/websites/git-deploy/github-actions) | **Yes (better later)** | Build runs on GitHub with an explicit project path, .NET SDK version, and path filters so `web/` changes do not redeploy the API. Matches the monorepo split. **Not scaffolded yet** — `open-decisions.md` still lists “no production pipeline”. |
| Static / Node Control Panel types | **No** | Wrong surface: API is ASP.NET Core; UI is Cloudflare Workers, not MonsterASP Node hosting. |

### Recommendation (now)

1. **Keep WebDeploy** (VS import or `dotnet publish` with the local profile) for production cutover of the API to `site56119` / `db42287`.
2. When the open decision on a production pipeline closes, add a GitHub Actions workflow that publishes **only** `TransfersCheck.Api` (not the sample root-level `dotnet publish`), with `dotnet-version` matching `net10.0`, and secrets below — never put the WebDeploy password in YAML.
3. Defer Control Panel Git deploy unless you confirm MonsterASP’s build image has the .NET 10 SDK and you are willing to manage PAT + Protected files there.

### GitHub secrets (placeholders — set in repo Settings → Secrets when adding Actions)

| Secret | Value from Control Panel Deploy card |
| --- | --- |
| `WEBSITE_NAME` | `site56119` |
| `SERVER_COMPUTER_NAME` | `https://site56119.siteasp.net:8172` |
| `SERVER_USERNAME` | `site56119` |
| `SERVER_PASSWORD` | WebDeploy password (rotate if leaked; update local profiles too) |

Control Panel Git deploy (private repo) also needs a GitHub PAT with `repo` scope — see [personal access token](https://help.monsterasp.net/websites/git-deploy/personal-access-token). Do not commit the PAT.

Suggested Actions publish shape (not checked in yet):

```yaml
# Illustrative only — do not commit passwords.
# - actions/setup-dotnet with dotnet-version: '10.0.x'
# - dotnet publish src/TransfersCheck.Api/TransfersCheck.Api.csproj -c Release -o ./publish
# - WebDeploy action with the four secrets above
# - paths filter: src/TransfersCheck.Api/**, src/TransfersCheck.Core/**, src/TransfersCheck.Infrastructure/**
```

## Database schema

The repo has no EF migrations. `SchemaBootstrap` (Infrastructure) runs at API startup and only adds what the model needs: missing tables (with seed rows), missing columns and missing indexes. It never drops or alters existing objects and is idempotent. Development and Test always run it. Production runs it only with `Database:EnsureSchema=true` (env `Database__EnsureSchema=true`); set it for the first deploy of a build that adds columns, otherwise a database created by an older build fails with `Invalid column name ...`.

## Auth and deploy notes

- Commerce identity uses Auth0 (ADR-018). Auth0 domain / client id / client secret are typed configuration outside the repo (user-secrets or environment). Do not commit them.
- Settings: `Auth0:Authority` (`https://<tenant>.auth0.com`), `Auth0:ClientId`, `Auth0:ClientSecret`, `Auth0:RedirectUri` (`<api origin>/api/auth0/callback`, also an Allowed Callback URL in the tenant). The API refuses to start when `Authority` is set and the other three are not. With `Authority` empty (local dev without a tenant) the old local door stays until wave 16.
- Tenant: Universal Login with the Database connection and the Google social connection; the API validates the RS256 id_token and opens a session only for a verified email.
- Owner microsite login does not use Auth0 (ADR-014). Punto de Alerta uses usuario/clave, not Auth0 SSO (ADR-013).
- Mercado Pago OAuth is collection-account linking, not login SSO.

## Not used by this product

- Checked-in GitHub Actions / Azure DevOps deploy secrets — no production pipeline in-repo yet (`open-decisions.md`)
- UI deploy is Cloudflare Workers (`web/`), separate from MonsterASP WebDeploy

## Source document

Legacy notes mirrored under `deploy/local/`; WebDeploy credentials refreshed from the MonsterASP Control Panel publish profile download (2026-10-24).
