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

1. **Keep WebDeploy** (VS import or `dotnet publish` with the local profile) for production cutover of the API to `site56119` / `db75697`.
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

## Cambio comercial ADR-019 a ADR-023 (25/10/2026)

Publicar API y web de la misma revisión. Respaldar SQL antes del despliegue. El bootstrap agrega BillingOrders, actividad técnica del comercio y posición/vencimiento/selección de renovación de puntos; asigna posiciones a puntos anteriores. Conserva AccessUntil y las filas históricas de ConsultaEvents, que ya no se consultan. Revisar puntos heredados adicionales: sin un vencimiento pagado quedan suspendidos. No se borran cuentas.

Eliminar las configuraciones obsoletas Commercial:WeeklyQueryCap y Commercial:WarningGap. Sync usa ButtonCooldown=10 s, PollInterval=30 s, QuietPollInterval=1 min, IdlePollInterval=5 min y TimerInterval=10 s. Ninguno integra el precio del plan.

Volver a publicar el corpus de ayuda actualizado mediante el flujo Owner existente: las filas ya publicadas no se sobrescriben por seed. Individual comprado queda pendiente de Iniciar Evento; los períodos ya vigentes se respetan hasta AccessUntil.

**Antes de habilitar ventas reales**, completar ADR-022: el encabezado de pruebas del webhook no implementa todavía la firma oficial productiva. No usar el retorno del navegador para acreditar. Revisar pagos adicionales confirmados tarde (pagos recibidos sin ActivatedAt); requieren resolución de habilitación/devolución sin modificar el vencimiento cotizado.

Push necesita HTTPS, configuración Firebase/VAPID y permiso explícito. Probar en Chrome real foreground, segundo plano, navegador cerrado según soporte del SO, revocación y token caducado. Sonido del SO y entrega con navegador cerrado no están garantizados. La campanita de la página requiere interacción. No se realizó publicación ni cobro productivo como parte de esta implementación.

## Preflight de producción — 25/10/2026

- Base nueva aprobada: **db75697**. La anterior **db42287** se conserva intacta; no desplegar el modelo nuevo sobre ella.
- Conexión externa: `db75697.public.databaseasp.net`; desde MonsterASP: `db75697.databaseasp.net`. Usar TLS con validación del certificado.
- `Test-ProductionDatabase.ps1` ejecuta solamente SELECT de metadatos y toma las credenciales de `deploy/local/database-db75697.local.json`. Nunca imprime la contraseña. `Test-ProductionWebsite.ps1` sólo consulta metadatos por WebDeploy.
- Se inicializó la base nueva vacía con `tools/TransfersCheck.DatabaseSetup`, sin iniciar API, Hangfire ni servicios externos. El comando exige nombre de base esperado y cero tablas; rechaza repetir la inicialización en una base poblada. Mantener `Database:EnsureSchema=false` en la configuración preparada.
- `TransfersCheck.slnx` incluye sólo los proyectos productivos. Para desarrollo y pruebas usar `TransfersCheck.Development.slnx`. MockServer no es publicable y rechaza entornos distintos de Development/Test; las rutas de cambio de plan de prueba se excluyen de Release.
- La API valida configuración productiva antes de iniciar servicios. Para validar sin conexión ni arranque: `dotnet TransfersCheck.Api.dll --validate-production-config <archivo-json>`.
- La configuración privada preparada está en `deploy/local/appsettings.Production.prepared.local.json`. Los archivos `*.local.json` no se cargan ni publican automáticamente: copiar explícitamente el archivo revisado como `appsettings.Production.json` al artefacto de publicación. Conservar `App_Data/keys` entre despliegues.
- Web: definir `VITE_API_ORIGIN=https://auth.transferscheck.com` antes del build. El build productivo rechaza localhost, HTTP, origen ausente y mocks habilitados. Los mocks push sólo se incluyen en modo de desarrollo con flag explícito; Playwright usa `.env.e2e`.
- Verificación: 72 pruebas .NET y 28 pruebas de navegador aprobadas, build Release y frontend correctos, Wrangler dry-run correcto, npm audit sin vulnerabilidades.
- Panel MonsterASP: site56119 pausado, runtime .NET 10 x64 disponible, wwwroot vacío. Sólo están vinculados transferscheck.com y www.transferscheck.com. DNS autoritativo externo en Cloudflare. Falta vincular auth.transferscheck.com, crear su DNS y emitir HTTPS, y revisar los callbacks permitidos de Auth0/Mercado Pago antes del arranque público.
- Ventas reales siguen condicionadas a ADR-022 (confirmación productiva de pagos). Push del sistema operativo requiere credenciales Firebase/VAPID y prueba en Chrome real. Este preflight no publica la aplicación.


### Dominio backend — avance del preflight

auth.transferscheck.com tiene CNAME a site56119.siteasp.net (Solo DNS) en Cloudflare, verificado por DNS público. Binding agregado en MonsterASP y certificado Let's Encrypt emitido hasta 23/01/2027, con renovación automática y redirección HTTPS. La prueba de autorización de Auth0 devuelve Callback URL mismatch: permitir https://auth.transferscheck.com/api/auth0/callback antes de publicar. No se cambió el DNS principal ni los registros de correo. No se subieron artefactos productivos.


Sitio iniciado para verificar HTTPS: certificado validado desde esta PC y respuesta HTTP 403 con wwwroot vacío. Esto verifica TLS y llegada al hosting; no constituye una prueba de salud de la API, que todavía no fue desplegada.


### Auth0 validado — 25/10/2026

Con confirmación del administrador, la aplicación existente SimpleCheck pasó de SPA a Regular Web Application y se agregó el callback exacto https://auth.transferscheck.com/api/auth0/callback conservando las URLs previas. Método Client Secret (Post), compatible con Auth0IdentityProvider; no se rotó ni reveló el secreto. Logout ya permite https://transferscheck.com/. La prueba /authorize con PKCE devuelve HTTP 200 y pantalla de ingreso sin Callback URL mismatch. No se completó todavía un login extremo a extremo contra API publicada.


### API publicada y acceso comprobado — 25/10/2026

API Release desplegada por WebDeploy en site56119, 129 elementos agregados y ninguna eliminación. Configuración privada revisada incluida explícitamente; conexión interna a db75697. HTTPS /api/auth0/config y /api/plans devuelven 200 y datos SQL reales. CORS acepta https://transferscheck.com con credenciales. /api/session, /api/logins y /api/billing sin sesión devuelven 401; appsettings.Production.json no es público (404). Swagger está publicado por la configuración actual.

El administrador completó el ingreso real desde /api/auth0/login. /api/session en el mismo navegador devolvió la cuenta autenticada con isOwner=true: callback, intercambio con secreto, PKCE, validación de identidad, persistencia SQL y cookie de sesión funcionaron. No se copiaron cookies ni tokens. El redirect final a la raíz todavía falla porque falta publicar y enrutar el frontend de Cloudflare; no constituye fallo de Auth0. Ventas reales siguen pendientes de ADR-022. No se realizaron pagos ni vinculación de cuentas Mercado Pago.


### Frontend publicado — 25/10/2026

Worker transferscheck desplegado con Wrangler. Versión verificada: 9ae0b2e1-43f4-4c20-a62f-23f9f7dcdf55. Rutas exactas transferscheck.com/* y www.transferscheck.com/*, registros raíz A y www CNAME conservados con proxy activado. auth.transferscheck.com continúa Solo DNS hacia MonsterASP. No se modificaron registros de correo. Las rutas quedan declaradas en web/wrangler.toml; workers.dev y previews deshabilitados.

Build con VITE_API_ORIGIN=https://auth.transferscheck.com, sin mocks. HTTP 200 en raíz, www, ruta directa /configurar y service worker. En el navegador, /api/auth0/login vuelve a https://transferscheck.com/ y reconoce el Admin real. /conectar presenta el onboarding desde la API; www conserva ruta/query al volver al hostname canónico antes de iniciar React. Sin errores ni advertencias en consola durante la verificación. Corregido el mensaje de período vencido para un Admin nuevo que aún no conectó Mercado Pago.

Pendiente comercial: firma/confirmación productiva de pagos ADR-022. No se completó vinculación Mercado Pago ni se realizaron cobros. Push requiere configuración Firebase/VAPID y validación real.
### Mercado Pago conectado y consultas verificadas — 25/10/2026

El administrador guardó en la aplicación TransfersCheck Notifier el callback https://auth.transferscheck.com/api/connections/mercadopago/callback conservando la URL anterior. PKCE activo; permisos read y offline access, sin write. La conexión real completó el retorno, persiste tras recargar y habilita el período de prueba.

La prueba manual encontró un segundo problema: /v1/payments/search respondía 200, pero algunos resultados no incluyen payer. MercadoPagoTransferSource exigía esa propiedad y fallaba con KeyNotFoundException. Ahora payer ausente, null o vacío conserva el pago y deja el nombre vacío para mostrar Desconocido; los montos permanecen intactos. Tres pruebas de regresión aprobadas, Release publicado y actualización real verificada con movimientos de la cuenta. Bloqueo temporal del botón observado. Stdout se habilitó sólo para diagnosticar y quedó deshabilitado al terminar.

No confundir esta verificación de lectura con cobros del abono de TransfersCheck: la confirmación productiva de pagos de planes sigue pendiente de ADR-022. No se hicieron transacciones financieras durante esta prueba.

### Verificación operativa — 25/10/2026

Owner habilitado en producción mediante correo y contraseña privados; `OwnerOptions.Enabled` se calcula desde ambos valores. Respaldo remoto previo y credenciales generadas permanecen en `deploy/local/`, fuera de Git. Login Owner verificado con HTTP 200, consulta de servicios y cierre de sesión.

SMTP requiere `Mail:FromAddress` (no `Mail:From`). Sigue pendiente de configuración y prueba de entrega. Checkout requiere el token cobrador: el secreto OAuth de Mercado Pago no lo reemplaza. Se corrigió el indicador del panel que confundía ambos; esta corrección de código requiere publicación. Firebase continúa opcional y sin configurar.

Verificación: 85 pruebas .NET existentes, prueba adicional del indicador Checkout, 28 E2E locales, 5 E2E productivas y 3 pruebas del service worker aprobadas; build frontend aprobado con el origen HTTPS productivo. No se realizaron cobros ni se enviaron correos. Se retiró la contraseña FTP incrustada del helper; debe suministrarse externamente.

### Continuación de configuración operativa — 25/10/2026

Token cobrador productivo guardado desde Owner en el almacén cifrado de SQL. Checkout Pro abrió Pack Mes por ARS 5.999; no se pagó ni se acreditó acceso. El administrador utiliza actualmente la misma cuenta Mercado Pago para SaaS y comercio: pago real y acreditación quedan pendientes de otra cuenta compradora.

SMTP publicado con Host=site56119.siteasp.net, Port=25, UseSsl=false y FromAddress=postmaster@transferscheck.com, sin credenciales según relay local del hosting. Owner reconoce Mail configurado. Entrega no comprobada: el puerto no pudo validarse desde esta PC externa. MX apunta a mail1113.mailasp.net, SPF incluye spf.mailasp.net y DMARC está en p=none.

Publicados los tres assemblies Release de develop 3b6492b, incluida la corrección del indicador de token cobrador. Se conservaron configuración, claves Data Protection y respaldos privados de DLLs y JSON.

Firebase existente: transferscheck-f2e77, app web registrada y VAPID existente; Cloud Messaging V1 habilitado, plan Spark. Configuración privada preparada pero no publicada hasta disponer del JSON de cuenta de servicio. Consola abierta para que el administrador genere la credencial.

Webhooks de Mercado Pago: URL productiva preparada para /api/billing/webhook y evento de pagos seleccionado, sin guardar. Pendiente intervención del administrador para generar la firma; después cargar Checkout:NotificationSigningKey y probar la validación. Hasta entonces sigue el mecanismo de URL firmada por preferencia y conciliación autenticada de ADR-022. No dar por probado el ciclo de cobro completo.

### Firebase y webhook oficial habilitados — 25/10/2026

JSON de cuenta de servicio del proyecto transferscheck-f2e77 validado y publicado en App_Data/firebase-service-account.json; acceso HTTP público comprobado en 404. Configurados Firebase web/VAPID, endpoint FCM V1 y scope firebase.messaging. Cuenta de servicio autenticada correctamente contra Google OAuth. Owner reconoce las cuatro tarjetas configuradas; esto no garantiza entrega de correo ni de notificaciones.

Firma oficial de Mercado Pago guardada en Checkout:NotificationSigningKey, con secretos fuera de Git. Simulador oficial de pagos a /api/billing/webhook: HTTP 200 OK. Pago ficticio 123456 devuelve applied:false. Se corrigió el caso de consulta 404 al proveedor sin ocultar errores 401 o 500; seis pruebas de respuesta de Mercado Pago aprobadas y patch Infrastructure publicado.

Registro push del navegador realizado y persistencia verificada tras recargar; campanita activada. Pendiente transferencia real de otra cuenta para verificar detección, sonido y entrega push, y compra del abono con comprador distinto. SMTP continúa configurado, entrega pendiente de prueba.
