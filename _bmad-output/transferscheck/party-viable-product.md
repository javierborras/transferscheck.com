# Party — viable product to start testing

Superseded 22 October 2026 by the parking-lot review where this note conflicts with `adrs.md`. The transcript below is the earlier party. It is not the current commercial lock.

**Further supersession 24 Oct 2026:** Auth0 for commerce is in scope (ADR-018). Do not treat the "Auth0" line under "Do not pull these in" below as current law for the commerce door.

22 October 2026. Party mode, subagent. No product code was changed. Epics and stories were not changed.

## Question

What is missing for a viable product the human can start testing with a real merchant, compared with the locked definitions?

The human (Spanish, Argentina) looked at `http://localhost:5183/` and said there are no menus, no logos, no onboarding, nothing: a dry screen. It is practical, and it does not meet the definitions.

Source of truth, in order: `brief.md`, `epics-and-stories.md`, `adrs.md`, `parking-lot.md`, `mp-authorization-setup.md`. `decisions.md` records debts and assumptions and does not outrank those five. Closed text in the first five was not reopened to change the product.

A gap is one of two things. The documents require it and the running app does not show it. Or the documents are silent and the human's complaint needs a decision. A step, a field, a menu, or a story that is not in the pack is not required.

## Who spoke

Inline cast. No saved scene. Ad-hoc, so this room has no memlog.

| Voice | Code | Lens |
| --- | --- | --- |
| Mary 📊 | bmad-agent-analyst | Whether the definitions are actually met |
| John 📋 | bmad-agent-pm | What a real merchant can try |
| Sofia 🎯 | bmad-agent-po | In, out, and acceptance |
| Sally 🎨 | bmad-agent-ux-designer | The counter and the first connect |
| Winston 🏗️ | bmad-agent-architect | What the ADRs already decided |
| Amelia 💻 | bmad-agent-dev | File paths against story ids |
| Smithy ⚒️ | bmad-toolsmith | What this review is for |
| Vex 🔒 | sec-hawk | Concrete failure paths |
| Grumbal 😤 | adversary | What pages someone at 3am |
| Boundary 🌶️ | edge-hunter | The second call |
| Yui 🎯 | craftsman | Do not build a second system |
| Dana 🚢 | shipper | What blocks a test, and what is polish |
| Wildcard 🃏 | option-generator | Options, then drop the ones that fight the pack |
| Level 📏 | claim-checker | What the files do not yet support |
| Killjoy 🛑 | loop-stopper | The question that matters |
| Splinter 🪵 | consensus-challenger | Which bar this session is using |

## What was on the glass

Walked 22 October 2026. Web `http://localhost:5183/` returned 200. API `http://localhost:5080/` and mock `http://localhost:5099/` answered. The signed-in tenant was already past setup.

The signed-in list showed the heading TransfersCheck, the line "La prueba está en curso.", ten rows (eight Llegó, Pendiente Uno Perez, Rechazado Dos Perez) with payer, amount, when, and status, the link Pagar, two test logins, Avisos and Revocar on the non-owner row, Correo, Contraseña, Agregar cuenta, and Actualizar. The human's shot is that page scrolled to the rejected row, the account form, and the line "Esperá un momento." There is no nav and no logo image.

`/pagar` showed the heading Pagar, ARS 5999, "cada 30 días", and one Pagar button. No in-page return.

`/cuenta` showed "Creá tu cuenta", Correo, Contraseña, and the submit button. It was not submitted. Enter was not opened: a session cookie was already present, and cookie clearing was not available. The enter screen is in `web/src/App.tsx` (`Entrá`, and a link to `/cuenta`).

`GET /api/checklist` for this tenant returned the brief's Spanish strings, every done flag true, `listReachable` true, `currentStep` `ready`, and `videoUrl` `""`.

`web/src/App.tsx`, `web/src/styles.css`, `web/index.html`, and `web/public/manifest.webmanifest` have uncommitted edits from another pass. The running paint is that working tree (light sheet, wide board). Committed `web/src/styles.css` is still a dark field with `main { max-width: 32rem }`. This note judges the screen the human is looking at. It does not treat the uncommitted stylesheet as a locked decision. The checklist unmount below is in both HEAD and the working tree.

PWA icons `/icon-192.png` and `/icon-512.png` are on disk under `web/public` and return 200. They are solid dark-green squares, not a drawn mark. A claim in the room that those files are missing is wrong.

## The argument

🚢 **Dana:** You can start on the mock today. The signed-in screen already shows the last 10, the statuses, the trial line, the pay link, account add, and Actualizar, and `/pagar` shows ARS 5999 cada 30 días. The checklist is hidden because this tenant is already connected and alerts are done, which is the story, and an empty `videoUrl` is allowed. A menu and a logo are not in the epics, and they do not block testing. A dry screen is a completed checklist. I will not let the room treat a menu or a logo as the gate.

📊 **Mary:** You're looking at a finished merchant, and that is the clue. The screen in front of you is the last 10 the brief already named under "UI rules" and "Done when." Menus and a logo are absent from the epics, so this dryness does not fail a definition we locked. What I will not blur is narrower. The connect step and the alerts step are required to show done, and this UI removes them the moment they finish, which is why a completed tenant looks as if onboarding was never built.

📋 **John:** The shot is the bottom of the last-10 screen for an owner whose checklist is already finished. Who, amount, when, Rechazado, Pagar, the extra-login form, and Actualizar are the counter job plus the owner controls the pack already placed on that screen. Why would a coat of paint tell them their money arrived? The one implementation miss is not this screenshot. The client drops the checklist as soon as connect is done, and alerts then render only while they are still open.

🎯 **Sofia:** Menus and a logo are not in the epics, so I am saying no. What fails acceptance is the checklist: the moment Mercado Pago is connected, that screen unmounts, so connect never shows done and alerts are not the next step of the same checklist. My pick for the state in the shot, where every step is already done: the checklist stays off the counter. The brief puts it before the list is useful, not on the counter forever.

🎨 **Sally:** The phone is low in the hand, past the money. The crop is the tail of the last ten, then Pagar, then the owner's mail, then another Correo and Contraseña. La prueba está en curso is status on the list. It is not the checklist. A step that has disappeared has not been shown done. The owner's own row has no Avisos control, and the first merchant is usually the owner standing at their own counter.

📏 **Level:** Hold the rule you are about to freeze. "Shows done" is a display verb in story 3.3, story 3.4, and brief steps 1 and 3. Those same lines say the last-10 list becomes reachable, and the brief says the merchant sees the checklist before the list is useful. Neither "the checklist stays up forever" nor "it may vanish" is written as a rule. `decisions.md` only assumes a done mark, on the connect screen, and no shot shows one. The pre-connect checklist was not screenshotted. Freezing that reading is how a dry list turns into a gap the files do not yet support. Menus, a logo, an empty video URL, a brand manual, and a semantic bot do not survive as open product work. Story 5.1 does: `/pagar` showed the one price.

😤 **Grumbal:** This will page someone at 3am, and the page will be a mock. Those rows are the mock seed, development OAuth points at `localhost:5099`, and there is still no real Mercado Pago client id or secret. "Esperá un momento." is the 429 line the contract already named. The list spends a manual refresh on first paint, so the counter button is dead for a minute. A menu, a logo, and a way back from Pagar are not in the epics, and I will not file them as bugs.

🌶️ **Boundary:** The list on this tenant already spent the refresh. Opening that screen posts a manual sync, which sets the minute lock, and the button stays live, so the next tap is the second call and that is the "Esperá un momento." line in the shot. I will not let that shot be read as Mercado Pago 429 or 2001, and I will not let the room "fix" it by writing `AccessUntil` from the pay return. The return is supposed to leave access unchanged.

🔒 **Vex:** I will not let a full list and an `initPoint` redirect close the real-merchant question. The mock seeds only the two transfer types and ignores `limit`, so ten rows on this screen are not the last ten transfers of a live account. The search asks for ten payments with no type filter, and the sync keeps `bank_transfer` and `account_money` after that page comes back. A logo does not move that.

💻 **Amelia:** `enableAlerts` never calls a push subscription. Empty `apiKey` stores `mock-<uuid>` and marks alerts done. A non-empty `apiKey` returns without a token. `web/public/sw.js` can show a notification and open `/?first=`, and nothing subscribes the browser. Do not smooth the dry list into a missing menu or a missing logo. Those are not acceptance criteria.

🎯 **Yui:** The five screens are already in the app: enter, `/cuenta`, the checklist, the last-10 list, and `/pagar`. The only heading is `TransfersCheck`. You reimplemented something that already exists: the list hardcodes `Activá los avisos`, the iPhone line, and `La prueba está en curso`, and the checklist payload already returns those strings. A menu will not connect Mercado Pago.

🏗️ **Winston:** The mock can walk the contract on this machine. It cannot stand in for their transfers. Until one real OAuth returns and a `MerchantConnection` exists for their account, the last 10 are not their money, and a menu will not change that.

⚒️ **Smithy:** This review is for one test: a real merchant connects their Mercado Pago and sees their own last ten. Menus, a logo, and a second onboarding are not in the epics. I will not add them.

🃏 **Wildcard:** Three small options are still open: sign in with a tenant that has not connected, so the existing Spanish checklist appears; put one text link on `/pagar` back to the list; and show the connect step as done once a connection exists, which story 3.4 already asks for. A navigation bar, an image logo, and a wizard or a chat do not help. Drop them. Keep the single return link. Once the app is on the iPhone home screen, the browser back button is gone.

🛑 **Killjoy:** The human asked what is missing before a real merchant can test, and the answer is not a new screen. Firebase, a container connection string, the certificate concession, and the `code_method` naming fight do not decide whether a test can start. The unresolved question is yes or no: once the Mercado Pago platform client id, client secret, and redirect URI are supplied outside the repo, do we start the real-merchant test on this screen, with no menu, no logo, and no checklist on a tenant that has already connected?

🪵 **Splinter:** The agreement treats this screen as the product. It is an already-connected tenant, so story 3.3's checklist is behind you. Assuming "therefore the product is viable" hides the bar you set when you asked to start with a real merchant. Which bar is this session for: the connected list and the one price on the mock, or the start of a real-merchant test, which stays closed until those operator credentials exist?

## Where they disagreed

**Does "shows done" fail this screen?** Mary, Sofia, Sally, Winston, Amelia, Smithy, Grumbal, and Yui say the client fails stories 3.3 and 3.4 because `Home` unmounts `ChecklistView` when `connectDone` is true, so the connect step is never painted as done, and the alerts action is a block on the list that disappears when `alertsDone` is true. John says that miss is real and is not the photograph: this tenant is finished, so both blocks are correctly off. Sofia splits it: after every step is done, the checklist stays off the counter; the failure is the moment after consent. Dana says bucket 1 is empty and hiding the checklist on a finished tenant is story 3.4. Level says neither reading is written down, the pre-connect screen was not shot, and the visual-pass sentence in `decisions.md` is an assumption, not a higher source. That did not close.

**What blocks the test?** Dana's fight is: a real merchant waits on Mercado Pago credentials and one real OAuth, and on nothing else in this room. Vex, Grumbal, Boundary, and Amelia add code that still fails after those credentials exist: the ten-payment window is unfiltered, the first paint spends the manual cooldown, the real web-key branch stores no push token, and a live webhook may not send the header this API requires. Winston agrees the mock is not their money, and he also names the client push hole.

**A way back from pay.** Mary, John, Sofia, and Grumbal close it. The pay screen's one verb is Pagar (`brief.md`, "UI rules"). Sally and Wildcard want one text link to `/` because `display: standalone` has no browser back, and story 5.3 sends the browser back with `AccessUntil` unchanged. Not closed.

**The owner's own Avisos box.** Sally and Boundary say `PushApproved` defaults false and the owner row renders no checkbox, so the owner's phone is never in the send list (story 6.5). The rest of the room did not take it up. Not closed.

**Icons.** Sofia, Amelia, and Vex said the icon files are absent from `web/public`. The walk found `icon-192.png` and `icon-512.png` there. They are solid green squares. That is not a logo, and it is not a missing file.

## What closed

- **Menus are not required.** `brief.md` "UI rules" names the screens: enter, create account, the setup checklist, last 10, pay. `epics-and-stories.md` has no menu. `decisions.md` already says the extra logins and push approval "are not a new menu and not a new story." ADR-008 refuses a role matrix. Closed. No proposal.
- **A logo is not required.** No epic and no ADR names one. `parking-lot.md` "Brand manual" keeps a brand manual out and limits MVP copy to the UI rules and the checklist. The word TransfersCheck is the `h1`. Closed. No proposal. Heavier type and an image mark were raised and dropped.
- **Onboarding beyond the checklist is not required.** `parking-lot.md` "Onboarding" keeps the semantic assistant out. The human rejected that assistant on 21 Oct 2026. Stories 3.3 and 3.6 are the onboarding: the checklist, the four numbered lines, and an optional video URL. ADR-004 says the connect step is a checklist plus an optional video, not a chat. Closed.
- **An empty video URL is not a defect.** `brief.md` setup step 2, story 3.6, and `decisions.md`: an empty `Onboarding:MercadoPagoVideoUrl` hides the player and leaves the steps and the button. The API returned `""`.
- **The pay screen meets story 5.1 on this machine.** One number, ARS 5999, "cada 30 días", one Pagar button, read from configuration.
- **The visible last-10 list meets the on-screen half of story 4.1 for this mock tenant.** Who, amount, when, status, ten rows, Actualizar large at the bottom. Level's caution stands for the worker, the type filter, and a thumb measurement. Those are in the code notes below, not in the photograph.
- **Parking-lot items stay out.** Listed in bucket 3. Do not pull them in to make the screen feel finished.
- **The mock can carry a local rehearsal. It cannot carry a real merchant.** See bucket 4.

## What stayed open

Handed back. The room does not decide these.

1. Killjoy's question, which is Splinter's bar: once the platform client id, client secret, and redirect URI exist outside the repo, is the test the screen that is up now (no menu, no logo, no checklist on a tenant that has already connected), or is the test not started until a real OAuth has stored a `MerchantConnection` for that merchant?
2. Whether "shows done" means the checklist remains visible after every step is done, or only that the connect step is marked done at the moment the list becomes reachable and alerts are still open. The smallest reading that still uses the story words is recorded below as a proposal. It was not adopted.
3. Whether `/pagar` gets one text link back to `/`. Proposal below. Not adopted.
4. Whether the owner row gets the same Avisos control as the other accounts. Proposal below. Not adopted.

An empty-list sentence was offered and dropped. `decisions.md` "Visual pass" already says this pass adds no new copy.

## 1. Required by the brief or the epics, absent or broken

These are implementation debts. They are not new stories.

### The connect step never shows done

`brief.md` "Setup checklist", step 3: "When the connection is stored, this step shows done and the last-10 list is reachable." Story 3.4: "When a `MerchantConnection` exists, the connect step shows done and the last-10 list is reachable." Story 3.3: alerts and the iPhone line are on that same checklist, and "The trial line is status" while alerts can still be the open step.

`web/src/App.tsx` renders `ChecklistView` only when `connectDone` is false. Inside that view the alerts row is a title. The permission button and the iPhone sentence render on the list, and only while `alertsDone` is false. The trial line renders on the list from a hardcoded sentence, not from the payload's `trialLine`. This tenant has every flag true, so the signed-in screen shows the list and the trial line and does not show a done step.

The pre-connect checklist was not screenshotted. The API for this tenant is past it. A new account before connect still has a checklist view in the client. Confidence is high that the post-connect moment does not paint "done". Confidence is low, as Level said, that the stories require the checklist to remain on a fully finished counter. See the proposal.

### The first paint spends the manual cooldown

Story 4.2 and ADR-006: the button runs the same read as the clock, and the default cooldown is 60 seconds. The worker calls the sync with `manual: false` (`TransferPollWorker`). The list's open effect posts `/api/transfers/refresh`, and that endpoint calls `Run(..., manual: true)` (`TransferEndpoints.Refresh`). A manual success sets `RefreshNotBefore` (`TransferSync`). The open call ignores the response, so the wait line does not appear by itself. The next tap inside the minute returns 429, and the click handler sets "Esperá un momento."

The sentence is the one `decisions.md` already named. It is not a missing splash. The defect is that opening the list counts as the button press. The human's shot is consistent with a second tap, not with a Mercado Pago 429. That distinction was not proven against the server log.

### The last 10 of the two types are taken from an unfiltered page of 10

Story 4.1 and `brief.md` "Scope": the last 10 incoming transfers of types `bank_transfer` and `account_money`. `MercadoPagoTransferSource.ReadIncomingAsync` requests `/v1/payments/search` with `limit={pageSize}` and no type parameter. `TransferSync` keeps the seeded type codes after that page returns. There is no second page. The mock list on screen does not show this. A live account whose ten newest payments are not all of those two types can omit a transfer the counter needs. Vex's claim matches the code. It was not reproduced against a live Mercado Pago account, because that account is not connected.

### A real web push key does not register a token

Story 6.2 and ADR-003: the PWA registers, and the API sends FCM HTTP v1 after a transfer is stored. `enableAlerts` stores `mock-<uuid>` only when `apiKey` is empty, which is the stand-in `decisions.md` already recorded. When `apiKey` is set, the token is empty and the function returns without `POST /api/push/tokens`. Nothing in the page calls `pushManager.subscribe`. An open tab can still beep when a stored row becomes `approved` (`announceArrivals`). A locked phone gets no system notification from this button. This tenant's alerts step is done because a mock token was stored.

### The owner cannot approve the owner's phone

Story 6.5: push goes only to accounts the owner has approved. The owner is one of the five (`brief.md` "Who This Serves"). Signup creates the owner with `IsOwner = true` and does not set `PushApproved` (`AccountEndpoints`), so it stays false. The list skips Avisos and Revocar when `row.isOwner` is true. `EfMerchantStore` sends only where `PushApproved` is true. The table did not close the remedy. The code fact stands.

### Not in this bucket

Enter, Creá tu cuenta, the last-10 row contents, Actualizar, the trial line on this finished tenant, the price screen, and the empty video player. The uncommitted light layout is not a requirement and is not a decision.

## 2. Silent in the locked docs, and the human now expects it

| Wish | In the epics? | Table |
| --- | --- | --- |
| Menus | No. Screens are named in `brief.md` "UI rules". Extra logins are already "not a new menu" in `decisions.md`. | Closed. Not required. |
| Logo | No. Brand manual is in `parking-lot.md`. | Closed. Not required. Solid green icons are the PWA install files, not a mark. |
| Onboarding beyond the checklist | No. The checklist and the optional video are the MVP. The assistant is parked. | Closed. Not required. This screen is dry because this tenant is past connect, not because a tour was specified and skipped. |
| A way back from Pagar | Not named. "Each screen has one primary verb." | Not closed. Proposal below. |
| Owner Avisos on the owner's own row | Story 6.5 is not silent about approval. The control's absence is bucket 1. The remedy was not agreed. | Proposal below. |
| Empty-list copy, a hero, a splash, heavier wordmark | Not named. Visual pass: no new copy. | Dropped. |

**Proposal, not adopted.** When a connection is stored, mark the connect step done, keep Activá los avisos and the iPhone line on that same checklist until a push token exists, and keep the last-10 list reachable. Once alerts are done, the counter screen is the list and the trial line, without a permanent checklist. No menu, no logo, no tour. This does not add a story. It is the smallest reading of "shows done" that does not leave the checklist on the counter forever. Sofia wanted the second half. Mary and Sally wanted the done mark. Level said the forever reading is not in the file.

**Proposal, not adopted.** One text link from `/pagar` to `/`. Not a second button, not a menu, not a new screen. Sally and Wildcard. Mary, John, Sofia, and Grumbal said the brief does not ask for it.

**Proposal, not adopted.** The owner row uses the same Avisos control as the other accounts. The owner is still not revoked by that control. Sally and Boundary. The rest did not close it.

## 3. Already out of scope in the parking lot

Do not pull these in. `parking-lot.md`.

- Brand manual. A logo system, a splash, or copy beyond the UI rules and the checklist.
- Semantic onboarding assistant. Rejected 21 Oct 2026. The checklist is not a chat (story 3.4, `brief.md`).
- Auto-published help, a help publisher, and an FAQ CMS. The video is one typed URL.
- LLM support.
- Business-config admin UI. Price, intervals, and secrets stay in typed configuration (ADR-007).
- Store product: role matrix, tills, per-seat price. Five logins, one merchant (ADR-008).
- Native apps. The client is the React PWA.
- A second payment provider. The port stays; the only entry is `mercadopago` (ADR-005).
- 24-hour day pass, prepaid packs, a credit ledger, coupons, referrals, cashback. ~~Auth0~~ (commerce Auth0 in as of ADR-018, 24 Oct 2026; owner microsite and Punto de Alerta still out).
- The 500-transaction weekly cap. There is no usage cap.
- Architecture exclusions already in `adrs.md`: MediatR, a generic repository, Hangfire, SignalR, a key-value configuration bag, plan tables, and preapproval.

`mp-authorization-setup.md` is the operator film. It is not the merchant video and it is not shown to cashiers.

The static checklist and `Onboarding:MercadoPagoVideoUrl` are in the MVP. They are not items to pull off the parking lot. On this tenant the video URL is empty, which the brief allows.

## 4. Operational debt already in `decisions.md`

Testing against the mock can start on this machine. A real merchant cannot start.

`decisions.md` "Party — preflight operational data" says Mercado Pago and Firebase stay on the mock until real credentials exist, and that real OAuth and real push do not block the rest of the MVP. That sentence is permission to keep building. Splinter is right that it is not a pass of a real-merchant test. `brief.md` "Done when" is a person at the counter seeing that merchant's incoming transfers.

| Debt, already recorded | This machine, mock | A real merchant |
| --- | --- | --- |
| Platform client id, client secret, redirect URI. No test account named. Operator checklist step 7 not run (`mp-authorization-setup.md`). | Connect and the ten rows are the mock. Rehearse signup, the checklist on a new tenant, the list, refresh, and the price. | Their transfers are not in this list. |
| `Checkout:AccessToken` absent. | `/pagar` shows ARS 5999 cada 30 días. The mock covers the preference and the webhook. | A real charge cannot be taken. The trial starts when Mercado Pago is connected, so watching the list does not need the charge (`brief.md` "Commercial terms"). |
| Firebase service-account JSON absent. Empty web keys store a mock token. | The alerts step can be marked done. An open tab can beep. | A locked phone does not ring. The client hole in bucket 1 remains after the JSON is supplied, until the page actually subscribes. |
| No Cloudflare account. Workers deploy was not run. UI origin is `http://localhost:5183`. | This PC can run the test. | A counter phone cannot open this machine. iPhone push needs Add to Home Screen on the UI origin (story 6.1, ADR-003). |
| `Onboarding:MercadoPagoVideoUrl` empty. | Player stays hidden. Connect stays available. | Same. Not a blocker. Film the consent path separately if a player is wanted. Do not put the operator script in that URL. |

Proof secrets under `D:\Compartida\SimpleCheckMercadoPago` stay out. Rotate them. Do not reuse them here. Do not print them.

One risk already sits on a recorded choice, and it is not a new product rule. `decisions.md` "Party — unnamed contract details" says the webhook extends `AccessUntil` only when the shared secret matches, and the secret travels in `X-Webhook-Secret`. The browser return does not change `AccessUntil` (story 5.3). A live Checkout Pro notification may not send that header. If it does not, a real payment will not extend access even after `Checkout:AccessToken` exists. The room did not rename the header. Boundary's warning stands: do not "fix" that by writing access from the browser return.

`code_method` versus `code_challenge_method` stays inside the real-OAuth debt. The product sends `code_challenge_method`. If a live application requires the other name, that rename is not a new story.

## Evidence the room did not have

- Enter was not screenshotted.
- The checklist was not screenshotted on a tenant with `connectDone` false, or on a tenant with connect done and alerts still open.
- No live Mercado Pago account was connected. The ten rows are the mock seed.
- The uncommitted stylesheet was on screen. Committed CSS is still the 32rem dark column. Layout polish from that dirty tree is not part of this verdict.
