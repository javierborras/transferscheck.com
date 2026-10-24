# Party — old TransfersCheck UI salvage, 23 October 2026

Adopted by the human on 23 October 2026. The human accepted Rescatar and Dejar as written. This file is that list. It does not add an epic. It does not change `src/` or `web/`. Folds that change an existing check are in `decisions.md`, `adrs.md`, and `epics-and-stories.md`. The weekly-cap cutoff was not set by this party. The human closed it the same night. ADR-007.

**Supersession 24 Oct 2026:** the Dejar items that forbade Auth0 and Google on the commerce door are withdrawn. Commerce identity is Auth0 (ADR-018). Owner microsite and Punto de Alerta rules that stay out of Auth0 are unchanged. The transcript below is historical evidence; do not treat the "Out: Auth0, Google" leave as current law for commerce.

Active room: the installed party, session mode. The human was not in the room for each turn. Voices who spoke: Mary, John, Sofia, Sally, Winston, Amelia, Murat. Summoned for this pass: Vex, Level, Killjoy, Splinter. Other groups exist (The Product Team, Code Review Crew, Anti-Consensus Club) and were not switched to. Splinter was summoned because an easy list would have looked like a vote.

The skill does not let the room replace the human. The same installed room handed back the permission comma earlier the same day. That later adoption was the human's. It is not a precedent that this list is already law.

Evidence, and only evidence. The live site `https://transferscheck.com/dashboard` was walked read-only on 23 October 2026 with a temporary login. It is the previous product, not the source of truth. The UI code is `WebSaaS/simple-check-client/src` in the old SimpleCheck repo. Closed decisions from the transferscheck pack, 22–23 October 2026, stay closed. Coupon, Subscription, AppConfiguration, Auth0, SignalR, and cashback in that old repo are not requirements. Where the pack already chose SignalR, the poll worker, or a referral, that choice is the pack's, not a revival of the old shape. Nothing is rescued unless it was on the walked screen or in the pages that render those screens.

## The table

📊 **Mary:** Two piles, and they do not match. On the glass: a Spanish door, then Auth0 in English at `simplecheck.us.auth0.com`, product name SimpleCheck, a Dev Keys warning, Google, sign up, reset. After that, an empty "Historial de Transacciones", a payments page whose Email row showed an Auth0 id, a settings door we did not open, a legal page, and a backend SMTP form we did not submit. In the React door the strings are "Hola de nuevo" and "Iniciar sesión con Google". The hosted Auth0 screen is not in `Login.tsx`. I can quote both. I will not treat the hosted page as copy we own.

📋 **John:** Why was the counter on that dashboard? To see whether the payment arrived. Fecha, Pagador, Monto, Estado. That is the job we already gave the row. The rest of the chrome is a second product asking to come back.

🎨 **Sally:** Picture the empty counter. The card says "No hay transacciones recientes." That sentence is the scene. Keep it. The three cards above the table, the search, the collapse — I can live without the cards. I do not want a mute table. "Desconocido" when the payer is blank is the line the cashier actually needs. We already know Mercado Pago often omits the sender.

🎯 **Sofia:** In, if the human accepts: that empty sentence, those four column names, "Desconocido", the status words, the login email and password labels, "Pagos", "Transacciones", "Configurar", "Salir", "Mi código", the Mercado Pago connected state, "Habilitar Notificaciones". Out: Auth0, Google, "gratis", cashback, "suscripción", the next charge, the SMTP page, the second password gate. I will not write a ticket.

🏗️ **Winston:** "Sincronizar" posts a sync and the empty card tells the human to do it. The pack left the poll on TransferPollWorker. A button that reaches into Mercado Pago from the page fights that. The footer badge is the other channel: SignalR while the page is open. Same word must not mean both. "Backend Config / Pruebas" overrides appsettings from the commerce browser. That page does not move to the owner microsite. Moving it would invent an owner tool nobody asked for. It goes.

💻 **Amelia:** `Dashboard.tsx` loads `limit: 10`, then paints "Historial de Transacciones". Do not copy that 10 onto the admin history. The 10 is already the alert-point default, from the pack, not from this fetch. The table's empty branch always says "No hay resultados para la búsqueda." even when the search box is empty. That is why the walk saw both sentences. `Payments.tsx` "Ver Planes" has no handler. `Navbar.tsx` prints `user.tenantName` beside the logo. "Empresa" is a value, not a label. Settings ships `Admin` / `Admin123`, then stores the replacement in `localStorage`.

🧪 **Murat:** I will not sign a test that the empty history says "no hay resultados para la búsqueda." The card sentence is the one that matched an empty account. I also will not sign "Desconectado" until the assertion says which one. The footer uses it for the socket. The settings card uses it for Mercado Pago. The payments row says "No conectado". Three strings, two facts. Medio is worse: the cards bind `bankIssuer`, the table binds `paymentTypeId`, and both headers say Medio.

🔒 **Vex:** The settings door is not a layout to keep. `DEFAULT_ADMIN_PASS` is in the bundle. The check runs in the browser. `sessionStorage.admin_authed` is the session. Anyone who opens the script owns the gate. That is the exploit path. It is not the commerce login, and it is not the owner microsite. Drop the gate. Do not restyle it.

📏 **Level:** Confidence is high on the strings in the files I can point at. Confidence is lower on the hosted Auth0 screen: we saw it, we do not own its markup. "Empresa" is not the alias. The pack already shows the alias as `#alias`, chosen once, confirmed, never edited. Rescuing "Nombre de la Empresa" plus "Actualizar" would contradict that. The Email label is fine. The value on that account was an Auth0 id. The component prints `profile.email`. The bug is the value, not the word Email.

🎨 **Sally:** Then the slot beside the logo can show `#alias`, and the word Empresa goes. I still want Buscar. Ten rows on a Saturday night, two payments for the same amount, the cashier types a name.

📋 **John:** The cashier's tool is the last N and one detail. The admin's tool is history, the CSV, and totals by day, month, and range. Search is a third tool. The range is how the admin finds a day. I am not building a finder on top.

🎯 **Sofia:** Search is out. "Contraer tabla" is out. The last-three cards are out. They repeat the table. Medio is out because the name lies. I am not asking the human to pick a column.

🪵 **Splinter:** You are tidy, and that is the risk. The hidden assumption on facturación is that a prepaid Mercado Pago link needs no CUIT on the commerce screen. Section 4 of the old terms says billing data is for invoices. This room is not a lawyer. Leaving Nombre, CUIT/CUIL, and Dirección off the webapp is a product cut, not a tax opinion. The other assumption is that day, month, and range make Buscar useless. Both cuts are ours. Hand them back. Do not call the list a decision, and do not write it into an ADR.

🛑 **Killjoy:** Stop. The human called this party because there were too many questions. Facturación and search are recommendations to leave, not questions. Password reset is the same: "Olvidé mi contraseña" points at `/forgot-password`, and `App.tsx` has no such route. Leave it. The email code on every login is already closed. Nobody opens the weekly-cap anchor. That was forbidden before we sat down.

📊 **Mary:** Register and the wizard were not on the walk. In the code, Register says "Crea tu cuenta gratis" and the wizard collects billing, a plan, and Mercado Pago again. I am not promoting them into the inventory as if we saw them. They fall under the same leave as "gratis" and the billing form.

🎯 **Sofia:** The list below is what I would accept in one reply. It is not accepted. Party recommendation, not adopted.

The human adopted Rescatar and Dejar the same night. The sentence above is what Sofia said in the room, before that acceptance.

## Rescatar

Adopted by the human on 23 October 2026. A rescued label is not an epic. Each line names the closed decision it serves.

### Login door (`Login.tsx`)

- "Hola de nuevo", "Inicia sesión en tu cuenta de TransfersCheck", "Dirección de email *", "Contraseña *", "Iniciar sesión", and the show/hide control on the password. Serves the commerce admin sign-in: email and password. The email code on every login was not on this screen. It stays closed. This salvage does not remove it.
- Centered card, one column. Layout for that same sign-in.

### Bar (`Navbar.tsx`, `AppFooter.tsx`)

- "Transacciones". Serves the rows the admin and the Punto de Alerta already have. It does not say "historial", so the alert point is not handed the admin history.
- "Configurar". Serves the commerce admin's own settings: Mercado Pago, and the Puntos de Alerta they already manage. Not a new settings product.
- "Pagos". Serves the pay screen that shows active plans.
- "Salir" (desktop) and "Cerrar sesión" (the menu). Serves leaving the session those logins open.
- The email, truncated, in the bar. Serves the same sign-in. The string must be the email. The Auth0 id seen in the Email row is not this.
- The slot beside the logo. Serves the alias that is chosen once and shown as `#alias`. The slot only. Not the value "Empresa".
- Footer "© {year} TransfersCheck" and the mailto that the walk showed as `soporte@transferscheck.com`. The component prints `supportEmail` from `/api/app-info`. Serves the product name already in use. Not a new support desk.
- Footer badge "En línea" / "Desconectado", meaning the live channel only. Serves SignalR while the page is open. It must not be read as Mercado Pago.

### Transactions (`Dashboard.tsx`)

- Page title "Historial de Transacciones", on the commerce admin history only. Serves admin history. The Punto de Alerta does not get this title. They have no history.
- Empty card: "No hay transacciones recientes." Serves that history and the alert-point list when there are no rows. The second sentence is not in this bucket.
- Columns Fecha, Pagador, Monto, Estado. Serves the last N rows, the detail of one of those rows, and the admin history. Not a new report.
- Status words already in the code: Aprobado, Rechazado, Pendiente, En proceso. Serves Estado on that same row.
- "Desconocido" when `payerName` is empty. Serves the closed fact that Mercado Pago often omits the sender name.
- Amounts with `es-AR` / ARS, dates as `dd/MM/yyyy HH:mm`. Layout of that same row.
- Error line "Error al cargar las transacciones". Serves a failed load of those rows.
- In-page toast from `NotificationToast.tsx`: "Transacción {estado}" plus payer (or "Desconocido") plus the ARS amount. Serves SignalR while the page is open. The browser `Notification` fired from the open dashboard is not in this bucket.

### Mercado Pago and notices (settings code, behind the door the walk did not open)

- "Conectar Mercado Pago", "Re-conectar cuenta", badge "Conectado", and the payments-page "No conectado". Serves the commerce admin connecting Mercado Pago. One state, these words. Not the footer socket badge, and not the Mercado Pago user id.
- The confirm already in the code: "¿Estás seguro que deseas re-conectar tu cuenta de Mercado Pago?" Serves that same reconnect.
- "Habilitar Notificaciones". Serves the browser notification when the page is closed. The sentence under it talks about avisos de cobro. Keep the button. Do not keep "push" or Firebase as the mechanism.

### Pay screen (`Payments.tsx`)

- The label "Email". Serves the commerce sign-in. The value on the walked account was an Auth0 id. Do not rescue that value.
- The label "Plan" for the active pack name. Serves the pay screen showing active plans, and Pack Evento / Pack Mes. The card title "Suscripción" is not in this bucket.
- "Mi código". Serves the referral code: the referrer's next Pack Mes is free once, only if still active. The code is not a balance.

### One legal sentence (`Legal.tsx`)

- The "Aviso Importante": TransfersCheck is an assistance tool, and the user is the only one who verifies that the funds actually landed in the bank or the wallet. Serves the product already closed: it shows payments, it does not credit the account. This is not an adoption of the old terms.

## Dejar

Adopted by the human on 23 October 2026. These contradict a closed decision, or they add a tool the closed decisions did not ask for. Leaving them is the adopted cut. It is not a question.

### Auth and doors

- The hosted Auth0 page: English copy, Google, sign up, reset password, Dev Keys warning, product name SimpleCheck. ~~No Auth0 on the commerce login.~~ Superseded 24 Oct 2026 (ADR-018: commerce Auth0 in). No Auth0 on the owner microsite (still current).
- "Iniciar sesión con Google" on the Spanish door, and the same button on Register. Same closed decision.
- "¿No tienes una cuenta? Crear cuenta gratis" and Register's "Crea tu cuenta gratis". Packs are prepaid. The payment link credits the owner's Mercado Pago account.
- "Olvidé mi contraseña". The route is not registered. The email code on every login is already the second step. A reset flow is not in the closed login.
- The whole "Acceso Administrativo" gate: Usuario, Contraseña, Verificar, "Ingrese las credenciales de gestión local", the first-login "Cambio de contraseña", "Nueva Contraseña", "Confirmar Contraseña", "Establecer Contraseña", minimum 6 characters, defaults `Admin` / `Admin123` in the client. Not the commerce email-code login. Not the owner microsite. Vex's path stands.
- Wizard "Completar Registro", personal data, billing, "Selecciona tu Plan", and a second Mercado Pago connect. Not on the walk. Onboarding is already one first-run pass. This is a second registration.

### Transactions chrome

- "Sincroniza tu cuenta para ver tus últimos movimientos." and the button "Sincronizar" / "Sincronizando...". The poll stays on TransferPollWorker.
- The inner title "Historial MERCADO PAGO". The admin page title is enough. The alert point must not gain a history caption.
- "Buscar...", the search-by-name-or-status filter, and "No hay resultados para la búsqueda." The last string is also the bug Amelia named: it shows when the list is empty. Admin finding is day, month, and range. The alert point is the last N and one detail.
- "Contraer tabla" / "Expandir tabla".
- Sort icons on the headers. Not on the walk as a behavior. Not a closed tool.
- The three summary cards (last 3, with Fecha / Medio / status). They repeat the table. The alert-point count is the last N, default 10, not 3.
- Column Medio. The cards use `bankIssuer`. The table uses `paymentTypeId`. No closed decision asks for a payment-method column.
- `limit: 10` on this dashboard fetch, as a cap on admin history. Admin history is not the alert-point window.
- The browser notification fired while the dashboard is open: "Nuevas Transacciones" / "Tienes N nueva(s) transacción(es)". Browser notification is for when the page is closed. The open page has the toast.

### Pay, referral, billing

- Page title "Pagos y Suscripción" and the card title "Suscripción". Prepaid. No preapproval, no automatic debit.
- "No tenés una suscripción activa." Same contradiction.
- "Ver Planes". No handler. The pay screen already shows active plans.
- "Miembro desde", "Último acceso". Not a closed fact. Membership wording rides the subscription.
- "ID Mercado Pago" and showing `mpUserId`. The alert point has no Mercado Pago account settings. The admin connects the account. They do not need the collector id on this card. Use "No conectado" / "Conectado" instead.
- "Trial termina". English, and it sits on a subscription object. The 21-day Pack Mes trial is already closed. These words are not how it is said.
- "Próximo cobro" and `currentPeriodEnd` as a next charge. Contradicts prepaid and no automatic debit. Pack Evento is 24 hours. Pack Mes is 30 days. That duration is already closed. This label is not the salvage.
- Card title "Referidos y Cashback", "Cashback acumulado", the `$0.00` balance, and "Compartí tu código y obtenés saldo a favor cuando tus referidos se suscriban." No cashback. No money balance. The referral is not a subscription bonus. "Mi código" is the line that stays in Rescatar.
- "Datos de Facturación": Nombre / Razón Social, CUIT/CUIL, Dirección, "Actualizar Facturación", and the success and error lines around it. No closed decision gives the commerce webapp an invoice form. Leaving it off is the cut Splinter named. It is not a legal ruling.
- "Datos de la Empresa", "Nombre de la Empresa", "Actualizar", and the navbar value "Empresa". The alias is `#alias`, once, immutable. An editable company name is a different field. Do not map them.

### Legal page body

- Sections 1 through 5 as product copy, including Ley 25.326 tied to billing data, CABA jurisdiction, MonsterASP, and Auth0 as named dependencies.
- "Las suscripciones son recurrentes y pueden ser canceladas en cualquier momento desde el panel de usuario, evitando futuros cobros. No se realizan reembolsos por períodos ya transcurridos." The closed rule is stronger and different: no cash refund, no automatic debit, no preapproval. This paragraph brings periods and future charges back.
- "El usuario es responsable de realizar sus propios respaldos de transacciones importantes." The admin already has the CSV. This sentence is not a backup feature, and it should not become one.
- The link "Términos y Condiciones | Política de Privacidad" is not rescued as an obligation to adopt this document. The one sentence in Rescatar can be reused. The old page is not.

### Backend

- The entire "Backend Config / Pruebas" screen: SMTP test, MonsterASP, "Método de envío (Sobrescribe appsettings)", "Enviar Correo de Prueba". Commerce-facing SMTP and appsettings. The services panel stays on the owner microsite. This form is not that panel.

## Preguntar

None. Killjoy's cut, and the human asked not to face a pile of questions.

Named assumptions, adopted with the list on 23 October 2026. They were not questions.

- Facturación stays off the commerce webapp. If the human wants invoices, that is a later yes. This party does not ask.
- Buscar stays off. Day, month, and range stay the admin's finder. The last N stays the alert point's.
- The weekly-cap cutoff instant was still open when this party sat down. It was not on these screens. This party does not set it. Superseded the same night by the human, outside this party. ADR-007. The cutoff instant is the moment Mercado Pago is linked.

## Not done

No ADR. No epic. No edit under `src/` or `web/`. No commit. No keepsake page. This file is the record.
