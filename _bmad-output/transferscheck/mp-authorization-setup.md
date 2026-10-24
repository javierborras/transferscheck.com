# Mercado Pago authorization setup

Locked 21 October 2026. Source: human decision 21 Oct 2026.

This file is the film script and the internal checklist for the one platform Mercado Pago application. It is for the person who deploys transferscheck. It is not shown to a Punto de Alerta. Do not put this recording in `Onboarding:MercadoPagoVideoUrl`.

The merchant video is the four lines in `brief.md`: manage that account, tap Connect, sign in and accept, return here. Film that separately if you want a player on the connect step. This script is the other film: the one-time operator setup.

No secret values belong in this file, in the repo, or on camera. Cover the Client Secret, the authorization code, and the tokens, or stop the recording.

Merchants never create a Mercado Pago application, never paste a client secret, and never upload a JSON. One platform application. Merchants only grant OAuth consent.

Do not use the documented AI agent, the Asistente de Mercado Pago Developers, or an MCP prompt to create the application.

## Operator checklist

1. Sign in to Mercado Pago Developers on the platform account and open Tus integraciones.
2. Create one application manually.
3. Set URLs de redireccionamiento to the static https callback.
4. Enable Usar el flujo de código de autorización con PKCE.
5. Put the Client ID and Client Secret in user-secrets, appsettings, or the environment.
6. Rotate any client id or client secret that was in the old proof.
7. Confirm with one real OAuth. The connect step shows done only when a `MerchantConnection` is stored.

## Film script

Say the step number, do only the clicks named below, and read labels from the screen when this file marks the click unverified.

### 1. Open the developer panel

On camera: Mercado Pago Developers, signed in as the platform account. Not a Punto de Alerta account and not a commerce account you are about to connect.

Do:

- In the upper right, click **Ingresar** and sign in with the platform Mercado Pago account.
- Click **Tus integraciones**.

Source: [Credenciales](https://www.mercadopago.com.ar/developers/es/docs/your-integrations/credentials), section Compartir credenciales, step 1. The same labels are on [Credentials](https://www.mercadopago.com.ar/developers/en/docs/credentials) as Log in and Your integrations.

### 2. Create the application

On camera: the application list. Say that this application is the only one merchants will consent to.

Do: create the application manually from the **Panel de integración**.

The current pages name that manual option and do not list the wizard clicks after it. Unverified: every click after Panel de integración, including the name field, Pagos online, desarrollo propio, and any product such as Checkout Pro. An older indexed copy of the Checkout Pro create-application page listed those clicks; the page fetched on 21 Oct 2026 does not. Do not follow a Checkout Pro product path for this OAuth application unless the screen in front of you is the one you are configuring for authorization code. Read the labels that are actually on screen. Do not invent a menu.

Identity verification or reauthentication during creation was on that older indexed copy and was not on the page fetched on 21 Oct 2026. Unverified.

Sources:

- [Panel del desarrollador](https://www.mercadopago.com.ar/developers/es/docs/your-integrations/dashboard). Creating an application is named as manual from the Panel de integración. Each card shows the name and the application number and has a button to **Detalles de aplicación**.
- [Crear aplicación (Checkout Pro)](https://www.mercadopago.com.ar/developers/es/docs/checkout-pro/create-application). Same three creation options. The fetched page documents the AI path and does not document the manual clicks.

### 3. Set the redirect URI

On camera: the application, then edit, then advanced settings. Do not show other applications' secrets.

Do:

- Open the application card and go to **Detalles de aplicación** / **Datos de integración**.
- Click the edit control. The application-details page calls it the edit button above the application name, in the left menu. The OAuth page says **Editar** on **Detalles de aplicación**. Use the control that is on the screen. Both names are documented.
- Under **Configuraciones avanzadas**, set **URLs de redireccionamiento** to the static https URL that the app will send as `redirect_uri`. It is the same string as the redirect in typed configuration. https. Static. No extra query on that URL.
- Leave **Permisos de la aplicación** as created. The documented permissions are lectura, acceso offline, and escritura, all on by default. This setup does not change them.

Sources:

- [Datos de integración](https://www.mercadopago.com.ar/developers/es/docs/smartapps/resources/application-details). Field name **URLs de redireccionamiento**. https. Static URL. Used for Marketplace or the Authorization code flow. Edit control: the edit button above the application name.
- [Obtener Access Token](https://www.mercadopago.com.ar/developers/es/docs/security/oauth/creation). Step 1 under Obtener token: edit the application so it contains the redirect URLs. The sample field name on that page is also **URLs de redireccionamiento**. `redirect_uri` must be that static URL.
- [Buenas prácticas](https://www.mercadopago.com.ar/developers/es/docs/security/oauth/best-practices). If `redirect_uri` does not match the application configuration exactly, the call returns an error. Extra data goes in `state`, not in the redirect URL.

### 4. Enable PKCE

The OAuth introduction and the creation page say PKCE is not mandatory. They recommend it. After the field is enabled, Mercado Pago requires `code_challenge` and `code_method` on OAuth requests. This product already requires authorization code with PKCE, so enable the field.

Do: on **Detalles de aplicación**, click **Editar**, and enable **Usar el flujo de código de autorización con PKCE**.

The operator does not generate the verifier. The server does, stores it with `state` until expiry, and sends the challenge. The creation page says to use S256 when possible: `code_challenge` is BASE64URL after SHA256 of the verifier. The verifier is a random 43 to 128 character string. The sample authorization URL parameter is `code_challenge_method`. The prose on the same page also says the required field is `code_method`. Ship the parameter names the enabled application accepts. Do not put a verifier in this file.

Sources:

- [Obtener Access Token](https://www.mercadopago.com.ar/developers/es/docs/security/oauth/creation), section Configurar PKCE.
- [Datos de integración](https://www.mercadopago.com.ar/developers/es/docs/smartapps/resources/application-details), advanced setting **Usar el flujo de código de autorización con PKCE**.
- [OAuth](https://www.mercadopago.com.ar/developers/es/docs/security/oauth/introduction), PKCE note.

### 5. Store the client id and the client secret

`client_id` in the authorization URL is the application number. **Datos de integración** calls it **Número de aplicación**.

The token request also sends `client_secret`. The renew section of the credentials page says production credentials include the pair **Client ID** and **Client Secret**, reached through **Tus integraciones**. Renewing that pair renews both values.

Do:

- If production credentials are not active, activate them with the documented clicks: **Tus integraciones**, select the application, **Datos de integración**, section **Credenciales** on the right, **Producción**, **Activar credenciales**. The alternate path on that page is **Credenciales de producción** in the left menu. Then **Industria**, **Sitio web**, accept **Declaración de Privacidad** and **Términos y condiciones**, complete the reCAPTCHA, and click **Activar credenciales de producción**.
- Copy **Client ID** and **Client Secret** into user-secrets, appsettings, or the environment. The keys are the existing Mercado Pago client id and client secret settings.
- Do not copy **Public Key** or **Access Token** into those settings. The payouts page, after activation, names Public Key and Access Token only. It does not say the Client Secret is on that paragraph.

Unverified: a single screen path that shows the Client Secret was not in the payouts "Usar credenciales de producción" paragraph. The credentials page's renew section tells you to select the **Client ID** and **Client Secret** pair. Indexed text on that same credentials URL says production credentials display both pairs. A fetch on 21 Oct 2026 did not expand the production tab. If the pair is not on the screen after the steps above, stop. Do not guess another menu.

Sources:

- [Obtener Access Token](https://www.mercadopago.com.ar/developers/es/docs/security/oauth/creation). `client_id` replaces `APP_ID` with the application number. The authorization-code exchange sends `client_id` and `client_secret` to `POST https://api.mercadopago.com/oauth/token` with `grant_type` `authorization_code`. The server does that exchange. Do not run it on camera.
- [Datos de integración](https://www.mercadopago.com.ar/developers/es/docs/smartapps/resources/application-details). **Número de aplicación**.
- [Salir a producción (Payouts)](https://www.mercadopago.com.ar/developers/es/docs/payouts/go-to-production), section Activar credenciales de producción. Activation clicks are documented there. The following "Usar credenciales" paragraph names Public Key and Access Token.
- [Credenciales](https://www.mercadopago.com.ar/developers/es/docs/credentials) and [Credentials](https://www.mercadopago.com.ar/developers/en/docs/credentials), section Renovar credenciales / Renew credentials.

### 6. Rotate the proof secret

Any client id or client secret that was in the old proof is rotated before deploy. This pack does not record the old values or the new ones.

Do: if those proof values are the current **Client ID** and **Client Secret**, renew that pair. **Tus integraciones**, production credentials, select **Client ID** and **Client Secret**, click the three dots to the right, **Renovar**, then **Renovar ahora**. Both values in the pair change. Put the new values only in user-secrets, appsettings, or the environment. Remove the old values from source.

Source: [Credenciales](https://www.mercadopago.com.ar/developers/es/docs/your-integrations/credentials), section Renovar credenciales. English labels on [Credentials](https://www.mercadopago.com.ar/developers/en/docs/credentials): Renew, Renew now.

### 7. Confirm with one real OAuth

On camera: a Mercado Pago account you manage, the product button **Conectar Mercado Pago**, the Mercado Pago sign-in, and the return to transferscheck. Cut away before any secret, authorization code, or token is visible.

Do:

- Sign in to transferscheck as the owner.
- Tap **Conectar Mercado Pago**. The server opens the authorization URL. Do not paste that URL together with a client secret.
- Sign in to the Mercado Pago account you manage and allow access.
- You return to the redirect URI. The server stores the connection.
- The connect step shows done, and the last-10 list is reachable.

The consent screen is a Mercado Pago page. The creation page says the seller is sent to Mercado Pago, signs in, and allows access, and that the page states the scopes requested. Unverified: the button label on that consent screen. The docs do not name it. Say "permití el acceso" only if that is the control on screen; otherwise read the control that is there.

Documented authorization URL shape, from the PKCE section:

`https://auth.mercadopago.com/authorization?response_type=code&client_id=$APP_ID&redirect_uri=$YOUR_URL&code_challenge=$CODE_CHALLENGE&code_challenge_method=$CODE_METHOD`

The Obtener token sample also includes `state` and `platform_id=mp`. `state` is required by this product and is recommended by the best-practices page: unique per attempt, no sensitive data. `platform_id=mp` appears in that one sample and not in the PKCE sample. Unverified as a required parameter. It is not an operator step.

The authorization code lasts 10 minutes and is single-use. The server exchanges it. Do not copy it.

Sources:

- [Obtener Access Token](https://www.mercadopago.com.ar/developers/es/docs/security/oauth/creation). Authorization code, PKCE URL, seller sign-in, allow access, redirect `code`.
- [OAuth](https://www.mercadopago.com.ar/developers/es/docs/security/oauth/introduction). `authorization_code` lasts 10 minutes and is single-use. The flow is the seller explicitly authorizing access.
- [Buenas prácticas](https://www.mercadopago.com.ar/developers/es/docs/security/oauth/best-practices). `state` and exact `redirect_uri`.

## What the merchant sees

The merchant checklist and the Spanish lines are in `brief.md`. Cashiers do not see this file. The merchant never opens Tus integraciones for this setup.
