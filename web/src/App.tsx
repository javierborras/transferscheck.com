import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Link, Route, Routes, useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { claimPaymentNotice, showDesktopFallback, enableDeviceAlerts } from "./push";
import { Landing } from "./Landing";
import { BusinessSolutions } from "./BusinessSolutions";
import { LegalPage } from "./LegalPage";
import { Logo } from "./Logo";
import { PlanCards } from "./Plans";
import { playCashBell, unlockSound, soundEnabled, subscribeSound } from "./audio";
import { api, apiUrl, apiOrigin, withCorrelation } from "./api";
import { OwnerSite } from "./owner/OwnerSite";
import { NotificationBanner } from "./NotificationBanner";
import { liveTransferMessageType, type LiveTransferArrived } from "./liveTransferContract";

type Session = { accountId: string; tenantId: string; email: string; isOwner: boolean };
type Checklist = {
  accountTitle: string;
  connectTitle: string;
  alertsTitle: string;
  iosHint: string;
  trialLine: string;
  help: string;
  button: string;
  reconnectButton: string;
  reconnectConfirm: string;
  connectionBadge: string;
  accountDone: boolean;
  connectDone: boolean;
  alertsDone: boolean;
  trialRunning: boolean;
  listReachable: boolean;
  trialEligible: boolean;
  videoUrl: string;
};
type Transfer = {
  id: string;
  externalId?: string;
  payerName: string;
  amount: number;
  currency?: string;
  occurredAt: string;
  approvedAt?: string | null;
  status: string;
  statusLabel: string;
  statusDetail?: string;
  statusDetailLabel?: string;
  typeCode?: string;
  operationType?: string;
  paymentMethodId?: string;
  operationLabel?: string;
  coelsaId?: string;
  bankTransferId?: string;
  payerDocument?: string;
  payerEmail?: string;
  originBank?: string;
  originAccount?: string;
  description?: string;
};
type LoginRow = {
  id: string;
  email: string;
  displayName?: string;
  isOwner: boolean;
  pushApproved: boolean;
  revoked: boolean;
  passwordSet?: boolean;
  included: boolean;
  enabled: boolean;
  requiresPayment: boolean;
  extraAccessUntil?: string | null;
  renewWithAdmin: boolean;
};


const money = new Intl.NumberFormat("es-AR", { style: "currency", currency: "ARS", maximumFractionDigits: 2 });
const when = new Intl.DateTimeFormat("es-AR", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false
});

function Wordmark() { return <Logo />; }

function AppHeader({ onSalir, back, isOwner }: { onSalir: () => Promise<void>; back: boolean; isOwner: boolean }) {
  const [open, setOpen] = useState(false);
  const [alias, setAlias] = useState("");
  const location = useLocation();

  useEffect(() => {
    setOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    let gone = false;
    api("/api/alias").then(async (response) => {
      if (gone || !response.ok) return;
      const body = (await response.json()) as { alias?: string | null };
      setAlias(body.alias ?? "");
    });
    return () => {
      gone = true;
    };
  }, [location.pathname]);

  useEffect(() => {
    if (!open) return;
    const close = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [open]);

  const here = (path: string) => (location.pathname === path ? "page" : undefined);

  return (
    <header className="app-header">
      <Link className="wordmark" to="/">
        <Logo compact />
      </Link>
      <span className="role-indicator">{isOwner ? "Admin" : "Mostrador / Punto de Alerta"}</span>
      <span id="alias" className="alias-slot" aria-label="Alias">
        {alias}
      </span>
      <div className="header-actions">
        {back ? (
          <Link className="volver" to="/">
            Volver
          </Link>
        ) : null}
        <button
          className="menu-button"
          type="button"
          aria-expanded={open}
          aria-controls="app-menu"
          onClick={() => setOpen((value) => !value)}
        >
          Menú
        </button>
      </div>
      {open ? (
        <>
          <button className="menu-backdrop" type="button" aria-label="Cerrar menú" onClick={() => setOpen(false)} />
          <nav id="app-menu" className="app-menu" aria-label="Menú">
            <Link to="/" aria-current={here("/")}>
              Mostrador en vivo
            </Link>
            <Link to="/ayuda" aria-current={here("/ayuda")}>
              Ayuda
            </Link>
            {isOwner ? (
              <>
                <Link to="/historial" aria-current={here("/historial")}>
                  Movimientos y exportación
                </Link>
                <Link to="/usuarios" aria-current={here("/usuarios")}>
                  Puntos de Alerta
                </Link>
                <Link to="/pagar" aria-current={here("/pagar")}>
                  Mi Plan
                </Link>
              </>
            ) : null}
            <button className="salir" type="button" onClick={() => void onSalir()}>
              Salir
            </button>
            <button className="cerrar-sesion" type="button" onClick={() => void onSalir()}>
              Cerrar sesión
            </button>
          </nav>
        </>
      ) : null}
    </header>
  );
}

function Dock({ children }: { children: ReactNode }) {
  return <div className="dock">{children}</div>;
}

function Shell({
  onSalir,
  back,
  counter,
  isOwner,
  children
}: {
  onSalir: () => Promise<void>;
  back?: boolean;
  counter?: boolean;
  isOwner: boolean;
  children: ReactNode;
}) {
  return (
    <div className={counter ? "shell counter" : "shell page"}>
      <AppHeader onSalir={onSalir} back={back ?? false} isOwner={isOwner} />
      {isOwner && !counter ? <nav className="admin-nav" aria-label="Administración del comercio"><Link to="/">Mostrador en vivo</Link><Link to="/usuarios">Puntos de Alerta</Link><Link to="/historial">Movimientos y exportación</Link><Link to="/pagar">Mi Plan</Link><Link to="/conectar">Conectar Mercado Pago</Link></nav> : null}
      {children}
    </div>
  );
}

export function App() {
  const navigate = useNavigate();
  const location = useLocation();
  const [session, setSession] = useState<Session | null | undefined>(undefined);

  useEffect(() => {
    let gone = false;
    api("/api/session").then(async (response) => {
      if (gone) return;
      if (response.ok) {
        const selected = sessionStorage.getItem("initialPlan");
        if (selected) {
          const chosen = await api("/api/billing/initial-plan", { method: "POST", body: JSON.stringify({ planCode: selected }) });
          sessionStorage.removeItem("initialPlan");
          if (chosen.ok) navigate("/conectar");
        }
        setSession(await response.json());
        const first = sessionStorage.getItem("notificationFirst");
        if (first) { sessionStorage.removeItem("notificationFirst"); navigate(`/?first=${encodeURIComponent(first)}`); }
      } else setSession(null);
    });
    return () => {
      gone = true;
    };
  }, []);

  async function salir() {
    const response = await api("/api/sessions", { method: "DELETE" });
    if (!response.ok) return;
    setSession(null);
    if (response.status !== 204) {
      const body = (await response.json().catch(() => null)) as { federatedLogoutUrl?: string } | null;
      if (body?.federatedLogoutUrl) {
        window.location.assign(body.federatedLogoutUrl);
        return;
      }
    }
    navigate("/");
  }

  if (session === undefined) {
    return (
      <main className="gate">
        <Wordmark />
      </main>
    );
  }

  return (
    <Routes>
      <Route path="/ingresar" element={session ? <Transfers session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/cuenta" element={<CreateAccount onSignedIn={setSession} />} />
      <Route path="/invitar/:token" element={<ClaimInvite onSignedIn={setSession} />} />
      <Route path="/owner" element={<OwnerSite />} />
      <Route path="/soluciones-empresas" element={<BusinessSolutions />} />
      <Route path="/terminos" element={<LegalPage />} />
      <Route path="/privacidad" element={<LegalPage privacy />} />
      <Route path="/pagar" element={session ? <Pay session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/conectar" element={session ? <ChecklistPage session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/usuarios" element={session ? <Users session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/ayuda/:id?" element={session ? <HelpPage session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/historial" element={session?.isOwner ? <HistoryPage session={session} onSalir={salir} /> : session ? <Transfers session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="*" element={session ? <Transfers session={session} onSalir={salir} /> : new URLSearchParams(location.search).has("auth0_error") || new URLSearchParams(location.search).has("first") ? <Enter onSignedIn={setSession} /> : <Landing />} />
    </Routes>
  );
}

// Commerce identity is Auth0 Universal Login when the API has a tenant (ADR-018).
function useAuth0Enabled() {
  const [enabled, setEnabled] = useState<boolean | undefined>(undefined);
  useEffect(() => {
    let gone = false;
    api("/api/auth0/config").then(
      async (response) => {
        if (!gone) setEnabled(response.ok ? (await response.json()).enabled === true : false);
      },
      () => {
        if (!gone) setEnabled(false);
      }
    );
    return () => {
      gone = true;
    };
  }, []);
  return enabled;
}

function startAuth0(signUp: boolean) {
  const first = new URLSearchParams(window.location.search).get("first");
  if (first) sessionStorage.setItem("notificationFirst", first);
  if (signUp) { const plan = new URLSearchParams(window.location.search).get("plan"); sessionStorage.setItem("initialPlan", plan === "pack_evento" || plan === "pack_corporativo" ? plan : "pack_mes"); }
  window.location.assign(apiUrl(`/api/auth0/login${signUp ? "?signup=true" : ""}`));
}

function auth0Message(code: string | null, correlationId: string | null) {
  const text = (() => {
    switch (code) {
      case null:
        return "";
      case "email_unverified":
        return "Verificá tu email y volvé a intentar.";
      case "not_allowed":
        return "Esa cuenta no puede entrar como admin.";
      case "invalid_state":
        return "El inicio de sesión venció. Probá de nuevo.";
      case "server_error":
        return "No pudimos abrir tu sesión. Probá de nuevo en un momento.";
      default:
        return "No pudimos abrir tu sesión. Volvé a intentar.";
    }
  })();
  if (!text || (code !== "server_error" && code !== "exchange_failed")) return text;
  return correlationId && /^[A-Za-z0-9_-]{8,64}$/.test(correlationId) ? `${text} Referencia: ${correlationId}.` : text;
}
function CreateAccount({ onSignedIn }: { onSignedIn: (session: Session) => void }) {
  const navigate = useNavigate();
  const auth0 = useAuth0Enabled();
  const [error, setError] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const response = await api("/api/accounts", {
      method: "POST",
      body: JSON.stringify({ email: data.get("email"), password: data.get("password") })
    });
    if (!response.ok) {
      setError(response.status >= 500 ? withCorrelation(response, "No se pudo crear la cuenta.") : "No se pudo crear la cuenta.");
      return;
    }
    const current = await api("/api/session");
    if (!current.ok) {
      setError(withCorrelation(current, "No se pudo crear la cuenta."));
      return;
    }
    const selectedPlan = new URLSearchParams(window.location.search).get("plan"); await api("/api/billing/initial-plan", { method: "POST", body: JSON.stringify({ planCode: selectedPlan === "pack_evento" || selectedPlan === "pack_corporativo" ? selectedPlan : "pack_mes" }) });
    onSignedIn(await current.json());
    navigate(new URLSearchParams(window.location.search).has("first") ? `/?first=${encodeURIComponent(new URLSearchParams(window.location.search).get("first")!)}` : "/conectar");
  }

  if (auth0 === undefined) {
    return (
      <main className="gate">
        <Wordmark />
      </main>
    );
  }

  if (auth0) {
    return (
      <main className="gate">
        <Wordmark />
        <h1>Creá tu cuenta</h1>
        <p className="gate-lead">Registrate con tu email o con tu cuenta de Google.</p>
        <button type="button" onClick={() => startAuth0(true)}>
          Crear mi cuenta
        </button>
        <p className="switch">
          <Link to="/ingresar">Ya tengo cuenta</Link>
        </p>
      </main>
    );
  }

  return (
    <main className="gate">
      <Wordmark />
      <h1>Creá tu cuenta</h1>
      <form onSubmit={submit}>
        <label>
          Dirección de email *
          <input name="email" type="email" autoComplete="username" inputMode="email" required />
        </label>
        <label>
          Contraseña *
          <input
            name="password"
            type={showPassword ? "text" : "password"}
            autoComplete="new-password"
            minLength={8}
            required
          />
        </label>
        <button
          className="password-toggle"
          type="button"
          onClick={() => setShowPassword((value) => !value)}
        >
          {showPassword ? "Ocultar contraseña" : "Mostrar contraseña"}
        </button>
        {error ? <p className="error">{error}</p> : null}
        <button type="submit">Crear cuenta</button>
      </form>
    </main>
  );
}

function Enter({ onSignedIn }: { onSignedIn: (session: Session) => void }) {
  const navigate = useNavigate();
  const auth0 = useAuth0Enabled();
  const [params] = useSearchParams();
  const [error, setError] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [needsCode, setNeedsCode] = useState(false);
  const [alertPoint, setAlertPoint] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const payload: Record<string, FormDataEntryValue | null> = alertPoint
      ? { commerce: data.get("commerce"), user: data.get("user"), password: data.get("password") }
      : { email: data.get("email"), password: data.get("password") };
    if (!alertPoint && needsCode) payload.code = data.get("code");
    const response = await api("/api/sessions", {
      method: "POST",
      body: JSON.stringify(payload)
    });
    if (!response.ok) {
      if (response.status >= 500) {
        setError(withCorrelation(response, "No se pudo abrir la sesión."));
        return;
      }
      const body = await response.json().catch(() => ({} as { error?: string }));
      if (body.error === "code_required") {
        setNeedsCode(true);
        setError("Te enviamos un código a tu email. Ingresalo para continuar.");
        return;
      }
      setError(
        body.error === "invalid_code"
          ? "El código no sirve o venció."
          : alertPoint
            ? "El comercio, el usuario o la contraseña no sirven."
            : "El correo o la contraseña no sirven."
      );
      return;
    }
    const current = await api("/api/session");
    if (!current.ok) {
      setError(withCorrelation(current, "No se pudo abrir la sesión."));
      return;
    }
    onSignedIn(await current.json());
    navigate(new URLSearchParams(window.location.search).has("first") ? `/?first=${encodeURIComponent(new URLSearchParams(window.location.search).get("first")!)}` : "/");
  }

  if (auth0 === undefined) {
    return (
      <main className="gate">
        <Wordmark />
      </main>
    );
  }

  const adminAuth0 = auth0 && !alertPoint;
  const auth0Error = auth0Message(params.get("auth0_error"), params.get("correlation_id"));

  return (
    <main className="gate">
      <Wordmark />
      <h1>Hola de nuevo</h1>
      <p className="gate-lead">Ingresá a tu cuenta de transferscheck.com</p>
      {adminAuth0 ? (
        <>
          {auth0Error ? <p className="error">{auth0Error}</p> : null}
          <button type="button" onClick={() => startAuth0(false)}>
            Ingresar a mi cuenta
          </button>
        </>
      ) : (
      <form onSubmit={submit}>
        {alertPoint ? (
          <>
            <label>
              Comercio *
              <input name="commerce" autoComplete="organization" required />
            </label>
            <label>
              Usuario *
              <input name="user" autoComplete="username" required />
            </label>
          </>
        ) : (
          <label>
            Dirección de email *
            <input name="email" type="email" autoComplete="username" inputMode="email" required />
          </label>
        )}
        <label>
          Contraseña *
          <input
            name="password"
            type={showPassword ? "text" : "password"}
            autoComplete="current-password"
            required
          />
        </label>
        <button
          className="password-toggle"
          type="button"
          onClick={() => setShowPassword((value) => !value)}
        >
          {showPassword ? "Ocultar contraseña" : "Mostrar contraseña"}
        </button>
        {!alertPoint && needsCode ? (
          <label>
            Código *
            <input name="code" inputMode="numeric" autoComplete="one-time-code" required />
          </label>
        ) : null}
        {error ? <p className="error">{error}</p> : null}
        <button type="submit">Iniciar sesión</button>
      </form>
      )}
      <p className="switch">
        <button
          type="button"
          className="linkish"
          onClick={() => {
            setAlertPoint((value) => !value);
            setNeedsCode(false);
            setError("");
          }}
        >
          {alertPoint ? "Entrar como admin" : "Entrar como Punto de Alerta"}
        </button>
      </p>
      <p className="switch">
        <Link to="/cuenta">Creá tu cuenta</Link>
      </p>
    </main>
  );
}

function ClaimInvite({ onSignedIn }: { onSignedIn: (session: Session) => void }) {
  const navigate = useNavigate();
  const { token } = useParams();
  const [error, setError] = useState("");

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const response = await api(`/api/invites/${token}`, {
      method: "POST",
      body: JSON.stringify({ password: data.get("password") })
    });
    if (!response.ok) {
      setError(response.status >= 500 ? withCorrelation(response, "No pudimos usar el enlace. Volvé a intentar.") : "El enlace no sirve o ya se usó.");
      return;
    }
    const current = await api("/api/session");
    if (!current.ok) {
      setError(withCorrelation(current, "No pudimos usar el enlace. Volvé a intentar."));
      return;
    }
    onSignedIn(await current.json());
    navigate("/");
  }

  return (
    <main className="gate">
      <Wordmark />
      <h1>Creá tu contraseña</h1>
      <p className="gate-lead">Este enlace es de un solo uso.</p>
      <form onSubmit={submit}>
        <label>
          Contraseña *
          <input name="password" type="password" minLength={8} autoComplete="new-password" required />
        </label>
        {error ? <p className="error">{error}</p> : null}
        <button type="submit">Guardar e ingresar</button>
      </form>
    </main>
  );
}

function relativeTime(date: string) {
  const minutes = Math.max(0, Math.floor((Date.now() - Date.parse(date)) / 60000));
  return minutes < 1 ? "Hace instantes" : `Hace ${minutes} min`;
}

function helpLines(help: string) {
  const lines = help.split(/(?<=\.)\s+(?=\d+\.)/).map((line) => line.trim()).filter(Boolean);
  return lines.length > 1 ? lines : [help];
}

function ChecklistPage({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  const [checklist, setChecklist] = useState<Checklist | null>(null);

  async function reload() {
    const response = await api("/api/checklist");
    if (response.ok) {
      const state = await response.json();
      state.alertsDone = state.alertsDone && localStorage.getItem("push-account") === session.accountId;
      setChecklist(state);
    }
  }

  useEffect(() => {
    void reload();
  }, []);

  return (
    <Shell onSalir={onSalir} back isOwner={session.isOwner}>
      {checklist ? <ChecklistView checklist={checklist} onReload={reload} /> : <main />}
    </Shell>
  );
}

function ChecklistView({ checklist, onReload }: { checklist: Checklist; onReload: () => Promise<void> }) {
  const navigate = useNavigate();
  const [error, setError] = useState("");
  const connectNow = checklist.accountDone && !checklist.connectDone;
  const alertsNow = checklist.connectDone && !checklist.alertsDone;

  async function connect() {
    if (checklist.connectDone && !window.confirm(checklist.reconnectConfirm)) return;
    const response = await api("/api/connections/mercadopago", { method: "POST" });
    if (!response.ok) {
      setError(withCorrelation(response, "No se pudo empezar la conexión."));
      return;
    }
    const body = await response.json();
    window.location.assign(body.authorizationUrl);
  }

  async function enableAlerts() {
    try { await enableDeviceAlerts(); await onReload(); navigate("/"); }
    catch (error) { setError(withCorrelation(error, "No pudimos activar los avisos. Revisá los permisos de este navegador y volvé a intentar.")); }
  }

  const connectBody = (
    <>
      <p>Autorizá a transferscheck.com a leer únicamente las transferencias que entran a tu cuenta (bancarias y de Mercado Pago).</p>
      <div className="trust-block"><p><strong>🔒 Conexión oficial:</strong> No nos das tu clave. Autorizás directo en Mercado Pago.</p><p><strong>🙈 Modo ciego:</strong> Nadie en el mostrador podrá ver tu saldo total, tus gastos ni mover dinero.</p></div>
      <ol className="help">
        {helpLines(checklist.help).map((line) => (
          <li key={line}>{line}</li>
        ))}
      </ol>
      {checklist.videoUrl ? <iframe className="video" title="Mercado Pago" src={checklist.videoUrl} /> : null}
      {error ? <p className="error" role="alert">{error}</p> : null}
    </>
  );

  return (
    <>
      <main>
        <h1>Dejá listo tu mostrador en 3 pasos</h1>
        <p>Configuralo una sola vez. Tus empleados van a poder verificar cobros sin tocar tu cuenta de Mercado Pago.</p>
        {checklist.trialEligible ? <p className="trial-info">🎁 Tenés 21 días de prueba gratis en el Pack Mes desde el momento en que vinculás tu Mercado Pago.</p> : checklist.trialRunning ? <p className="trial-info">La prueba está en curso.</p> : null}
        <p className="connection-badge" data-connected={checklist.connectDone ? "yes" : "no"} role="status">
          {checklist.connectionBadge}
        </p>
        <ol className="steps">
          <li className={checklist.accountDone ? "done" : "current"} data-step="1">
            <div className="step-body">
              {checklist.accountDone ? "Creá tu cuenta de comercio" : <h1>Creá tu cuenta de comercio</h1>}
            </div>
          </li>
          <li className={connectNow ? "current" : checklist.connectDone ? "done" : "later"} data-step="2">
            <div className="step-body">
              {connectNow ? <h1>Conectá tu Mercado Pago de forma segura</h1> : "Conectá tu Mercado Pago de forma segura"}
              {connectNow || checklist.connectDone ? connectBody : null}
            </div>
          </li>
          <li className={alertsNow ? "current" : checklist.alertsDone ? "done" : "later"} data-step="3">
            <div className="step-body">
              {alertsNow ? <h1>Activá los avisos en este dispositivo</h1> : "Activá los avisos en este dispositivo"}
              <p>Habilitá las notificaciones para recibir un aviso cuando detectemos un pago acreditado.</p>
              <p className="ios-hint">{checklist.iosHint}</p>
              {alertsNow && error ? <p role="alert" className="error">{error}</p> : null}
            </div>
          </li>

        </ol>
      </main>
      {connectNow ? (
        <Dock>
          <button type="button" onClick={() => void connect()}>
            Conectar mi Mercado Pago
          </button>
        </Dock>
      ) : null}
      {checklist.connectDone && !alertsNow ? (
        <Dock>
          <button type="button" onClick={() => void connect()}>
            Reconectar Mercado Pago
          </button>
        </Dock>
      ) : null}
      {alertsNow ? (
        <Dock>
          <button type="button" onClick={() => void enableAlerts()}>
            Activar avisos
          </button>
        </Dock>
      ) : null}
    </>
  );
}

function Transfers({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  const [params] = useSearchParams();
  const [items, setItems] = useState<Transfer[]>([]);
  const [detail, setDetail] = useState<Transfer | null>(null);
  const detailDialog = useRef<HTMLDialogElement>(null);
  const [notice, setNotice] = useState<Transfer | null>(null);
  const [wait, setWait] = useState("");
  const [cooldown, setCooldown] = useState(0);
  const [loading, setLoading] = useState(false);
  const [trial, setTrial] = useState(false);
  const [expired, setExpired] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [fiveDayWarning, setFiveDayWarning] = useState(false);
  const [connected, setConnected] = useState(true);
  const [audioActive, setAudioActive] = useState(soundEnabled);
  const [audioError, setAudioError] = useState("");
  useEffect(() => subscribeSound(() => setAudioActive(soundEnabled())), []);
  const announced = useRef(new Set<string>());
  const [pollSeconds, setPollSeconds] = useState(30);
  const displayLimit = useRef(10);
  const [cooldownSeconds, setCooldownSeconds] = useState(10);
  const seen = useRef<Map<string, string> | null>(null);
  const first = params.get("first");

  // Modal via showModal() — not the open attribute (non-modal, no focus trap / Esc / backdrop).
  useEffect(() => {
    const dialog = detailDialog.current;
    if (!dialog) return;
    if (detail) {
      if (!dialog.open) dialog.showModal();
      return;
    }
    if (dialog.open) dialog.close();
  }, [detail]);

  async function load(announce: boolean) {
    const path = first ? `/api/transfers?first=${encodeURIComponent(first)}` : "/api/transfers";
    const billing = await api("/api/billing");
    if (billing.status === 401) { window.location.assign("/ingresar"); return; }
    if (billing.ok) {
      const state = await billing.json();
      setConnected(Boolean(state.connected));
      setExpired(Boolean(state.accessUntil) && new Date(state.accessUntil).getTime() <= Date.now());
      setBlocked(Boolean(state.blocked) || state.accountEnabled === false);
      setFiveDayWarning(Boolean(state.fiveDayWarning));
      if (state.blocked || state.accountEnabled === false) { setItems([]); return; }
    }
    const response = await api(path);
    if (response.status === 401) { window.location.assign("/ingresar"); return; }
    if (response.status === 403) { setItems([]); setBlocked(true); return; }
    if (!response.ok) return;
    const body = await response.json();
    const next = body.items as Transfer[];
    displayLimit.current = body.take ?? 10;
    setPollSeconds(body.pollIntervalSeconds);
    setCooldownSeconds(body.buttonCooldownSeconds);
    if (body.refreshRetrySeconds > 0) setCooldown(body.refreshRetrySeconds);
    if (announce) announceArrivals(next);
    else if (!seen.current) {
      seen.current = new Map(next.map(item => [item.id, item.status]));
      next.filter(item => item.status === "approved").forEach(item => announced.current.add(item.id));
    }
    setItems(next);
    const check = await api("/api/checklist");
    if (check.ok) {
      const state = await check.json(); setTrial(state.trialRunning);
    }
    return true;
  }

  function announceArrivals(next: Transfer[]) {
    const previous = seen.current;
    if (!previous) return;
    const arrived: Transfer[] = [];
    for (const item of next) {
      if (item.status === "approved" && previous.get(item.id) !== "approved" && !announced.current.has(item.id)) {
        announced.current.add(item.id);
        arrived.push(item);
      }
      previous.set(item.id, item.status);
    }
    arrived.forEach((item, index) => {
      void claimPaymentNotice(session.tenantId, item.id).then(claimed => {
        if (claimed) { playCashBell(index * 0.4); void showDesktopFallback(item.id, item.payerName, item.amount); }
      });
    });
    if (arrived.length) setNotice(arrived[0]);
  }

  useEffect(() => {
    let gone = false;
    void load(false).then(async ready => {
      if (!ready || gone) return;
      const response = await api("/api/transfers/refresh", { method: "POST" });
      if (response.ok && !gone) await load(false);
    }).catch(() => setWait("No pudimos cargar los movimientos. Revisá tu conexión."));
    let lastInteraction = 0;
    let lastLoad = Date.now();
    const activity = () => {
      if (document.visibilityState !== "visible") return;
      if (Date.now() - lastInteraction >= 10000) {
        lastInteraction = Date.now();
        void api("/api/activity", { method: "POST" }).catch(() => undefined);
      }
    };
    activity();
    const events = ["pointerdown", "keydown", "scroll"] as const;
    events.forEach(event => window.addEventListener(event, activity, { passive: true }));
    const onVisible = () => { if (document.visibilityState === "visible") activity(); };
    document.addEventListener("visibilitychange", onVisible);
    const onPush = (event: MessageEvent) => { if (event.data?.type === "transfer-arrived") void load(true); };
    navigator.serviceWorker?.addEventListener("message", onPush);
    const timer = window.setInterval(() => {
      const idle = Date.now() - lastInteraction;
      const interval = idle >= 600000 ? 300000 : idle >= 120000 ? 60000 : pollSeconds * 1000;
      if (!gone && Date.now() - lastLoad >= interval) { lastLoad = Date.now(); void load(true).catch(() => undefined); }
    }, 1000);
    const source = new EventSource(`${apiOrigin}/api/transfers/live`, { withCredentials: true });
    source.addEventListener("AccessChanged", event => {
      source.close(); setItems([]); setNotice(null); seen.current = null;
      const reason = JSON.parse((event as MessageEvent).data).reason;
      if (reason === "session-ended") window.location.assign("/ingresar");
      else { setBlocked(true); void load(false); }
    });
    source.addEventListener(liveTransferMessageType, (event) => {
      if (gone) return;
      const message = JSON.parse((event as MessageEvent).data) as LiveTransferArrived;
      if (message.messageType !== liveTransferMessageType) return;
      const row: Transfer = { id: message.id, payerName: message.payerName, amount: message.amount,
        occurredAt: message.occurredAt, status: message.status, statusLabel: message.statusLabel };
      announceArrivals([row]);
      setItems(current => {
        const updated = current.some(item => item.id === row.id)
          ? current.map(item => item.id === row.id ? row : item) : [row, ...current];
        return updated.sort((a, b) => Date.parse(b.occurredAt) - Date.parse(a.occurredAt)).slice(0, displayLimit.current);
      });
    });
    return () => {
      gone = true;
      window.clearInterval(timer);
      events.forEach(event => window.removeEventListener(event, activity));
      document.removeEventListener("visibilitychange", onVisible);
      navigator.serviceWorker?.removeEventListener("message", onPush);
      source.close();
    };
  }, [first, pollSeconds, session.isOwner]);

  useEffect(() => {
    if (cooldown <= 0) return;
    const tick = window.setTimeout(() => setCooldown((value) => value - 1), 1000);
    return () => window.clearTimeout(tick);
  }, [cooldown]);

  async function refresh() {
    if (cooldown > 0 || loading) return;
    setLoading(true);
    setWait("");
    try {
      const response = await api("/api/transfers/refresh", { method: "POST" });
      if (response.status === 429) {
        const body = await response.json().catch(() => ({}));
        setWait("Esperá unos segundos antes de volver a revisar los cobros.");
        setCooldown(body.retryAfterSeconds ?? cooldownSeconds);
      } else if (response.status === 409) { setExpired(true); setBlocked(true); }
      else if (!response.ok) setWait("No pudimos revisar los cobros. Volvé a intentar en un momento.");
      else { await load(true); setCooldown(cooldownSeconds); }
    } catch { setWait("No pudimos revisar los cobros. Revisá tu conexión y volvé a intentar."); }
    finally { setLoading(false); }
  }

  async function openDetail(id: string) {
    const response = await api(`/api/transfers/${id}`);
    if (response.ok) setDetail(await response.json());
  }

  const cooling = cooldown > 0;

  return (
    <Shell onSalir={onSalir} counter isOwner={session.isOwner}>
      <main className="board">
        <NotificationBanner accountId={session.accountId} />
        {blocked || expired ? (
          <section className="access-block" role="alert">
            <h1>{session.isOwner && !connected && !expired ? "Dejá listo tu mostrador en 3 pasos" : "Las alertas de tu mostrador están en pausa"}</h1>
            <p>{session.isOwner && !connected && !expired ? "Conectá tu Mercado Pago de forma segura para empezar a recibir avisos de tus cobros." : "Terminó el período activo de tu cuenta. Tus Puntos de Alerta configurados y tu historial están guardados y seguros."}</p>
            {!session.isOwner ? <p>Pedile al Admin del comercio que renueve el acceso.</p> : null}
            {session.isOwner ? <><Link to={connected ? "/pagar" : "/conectar"}>{connected ? "Ir a Pagos" : "Conectar Mercado Pago"}</Link></> : null}
          </section>
        ) : (
          <>
            <div className="board-scroll">
              <p className="update-hint">Actualización automática. Si el cliente ya pagó, podés revisar ahora.</p>
              {!audioActive ? <section className="sound-banner" role="status"><p>🔇 El sonido de caja está desactivado en este navegador.</p><button className="secondary" onClick={() => void unlockSound().catch(() => setAudioError("No pudimos activar el sonido. Tocá de nuevo para intentar."))}>🔊 Tocá acá para activar la campanita de cobros</button>{audioError ? <p role="alert">{audioError}</p> : null}</section> : <p className="sound-active">🔊 Alerta sonora activa</p>}
              {session.isOwner && fiveDayWarning ? (
                <p className="banner warning" role="status">
                  <span>Quedan cinco días de acceso. Pagá para seguir recibiendo avisos.</span>
                  <Link to="/pagar">Ir a Pagos</Link>
                </p>
              ) : session.isOwner && trial ? (
                <p className="banner trial">La prueba está en curso.</p>
              ) : null}
              {notice ? (
                <p className="notice" role="status">
                  <strong>¡Entró un pago!</strong>
                  <span className="money">{notice.payerName} te transfirió {money.format(notice.amount)}</span>
                </p>
              ) : null}
              <div className="transfer-columns" role="row">
                <span>Fecha</span>
                <span>Pagador</span>
                <span>Monto</span>
                <span>Estado</span>
              </div>
              {!items.length ? <p className="empty">Todavía no detectamos transferencias. Las revisamos periódicamente; también podés revisar ahora. Activá la campanita para escuchar los nuevos cobros.</p> : null}
              <ol className="transfers">
                {items.map((item, index) => {
                  const tone = item.status === "approved" || item.status === "pending" || item.status === "rejected" ? item.status : "other";
                  return (
                    <li key={item.id} className={`row ${tone} ${first === item.id ? "focus" : ""} ${index === 0 && tone === "approved" ? "latest-transfer" : ""}`}>
                      {index === 0 && tone === "approved" ? <span className="latest-label">✨ ÚLTIMO COBRO RECIBIDO · {relativeTime(item.occurredAt)}</span> : null}
                      <button type="button" className="row-open" onClick={() => void openDetail(item.id)}>
                        <div className="when">{when.format(new Date(item.occurredAt))}</div>
                        <div className="payer">{index === 0 ? "Pagó: " : ""}{item.payerName || "Desconocido"}</div>
                        <div className="amount">{money.format(item.amount)}</div>
                        <div className="status">{item.status === "approved" ? "✓ Acreditado" : item.statusLabel}</div>
                      </button>
                    </li>
                  );
                })}
              </ol>
            </div>
            <dialog
              ref={detailDialog}
              className="transfer-detail"
              closedby="any"
              aria-labelledby="transfer-detail-title"
              onClose={() => setDetail(null)}
              onClick={(event) => {
                // Safari still lacks closedby; light-dismiss by detecting backdrop clicks.
                if ("closedBy" in HTMLDialogElement.prototype) return;
                const dialog = event.currentTarget;
                if (event.target !== dialog) return;
                const rect = dialog.getBoundingClientRect();
                const inside =
                  rect.top <= event.clientY &&
                  event.clientY <= rect.top + rect.height &&
                  rect.left <= event.clientX &&
                  event.clientX <= rect.left + rect.width;
                if (!inside) dialog.close();
              }}
            >
              {detail ? <TransferDetailContent detail={detail} /> : null}
            </dialog>
            {wait ? <p className="wait" role="status">{wait}</p> : null}
            <Dock>
                <button
                  className={cooling ? "cooldown" : undefined}
                  type="button"
                  onClick={refresh}
                  aria-disabled={cooling || loading}
                  disabled={cooling || loading}
                >
                  {cooling ? `Esperá ${cooldown}s...` : loading ? "Actualizando..." : "🔄 Revisar si entró un pago ahora"}
                </button>
              </Dock>
          </>
        )}
      </main>
    </Shell>
  );
}

function TransferDetailContent({ detail }: { detail: Transfer }) {
  return (
    <div className="receipt-card">
      <div className="receipt-header">
        <h2 id="transfer-detail-title">Detalle del comprobante</h2>
        <span className={`status-pill ${detail.status}`}>
          {detail.status === "approved" ? "✓ Acreditado" : detail.statusLabel}
        </span>
      </div>
      <p className="receipt-amount money">{money.format(detail.amount)}</p>
      <p className="receipt-payer">{detail.payerName || "Desconocido"}</p>
      <p className="receipt-when">{when.format(new Date(detail.occurredAt))}</p>
      <p className="visually-hidden">{detail.statusLabel}</p>

      <dl className="receipt-grid">
        {detail.externalId ? (
          <div className="receipt-row">
            <dt>N.° de operación MP</dt>
            <dd className="mono">{detail.externalId}</dd>
          </div>
        ) : null}
        {detail.coelsaId ? (
          <div className="receipt-row">
            <dt>Código COELSA / ID</dt>
            <dd className="mono">{detail.coelsaId}</dd>
          </div>
        ) : null}
        {detail.bankTransferId ? (
          <div className="receipt-row">
            <dt>ID Transferencia bancaria</dt>
            <dd className="mono">{detail.bankTransferId}</dd>
          </div>
        ) : null}
        {detail.operationLabel ? (
          <div className="receipt-row">
            <dt>Tipo de operación</dt>
            <dd>{detail.operationLabel}</dd>
          </div>
        ) : null}
        {detail.payerDocument ? (
          <div className="receipt-row">
            <dt>CUIT / CUIL / Doc</dt>
            <dd className="mono">{detail.payerDocument}</dd>
          </div>
        ) : null}
        {detail.payerEmail ? (
          <div className="receipt-row">
            <dt>Correo del pagador</dt>
            <dd>{detail.payerEmail}</dd>
          </div>
        ) : null}
        {detail.originBank ? (
          <div className="receipt-row">
            <dt>Entidad de origen</dt>
            <dd>{detail.originBank}</dd>
          </div>
        ) : null}
        {detail.originAccount ? (
          <div className="receipt-row">
            <dt>CBU / CVU origen</dt>
            <dd className="mono">{detail.originAccount}</dd>
          </div>
        ) : null}
        {detail.description ? (
          <div className="receipt-row">
            <dt>Concepto / Motivo</dt>
            <dd>{detail.description}</dd>
          </div>
        ) : null}
        {detail.statusDetailLabel ? (
          <div className="receipt-row">
            <dt>Detalle de estado</dt>
            <dd>{detail.statusDetailLabel}</dd>
          </div>
        ) : null}
        {detail.approvedAt ? (
          <div className="receipt-row">
            <dt>Acreditado el</dt>
            <dd>{when.format(new Date(detail.approvedAt))}</dd>
          </div>
        ) : null}
      </dl>

      <form method="dialog">
        <button type="submit">Cerrar</button>
      </form>
    </div>
  );
}

function HistoryPage({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  const [items, setItems] = useState<Transfer[]>([]);
  const [detail, setDetail] = useState<Transfer | null>(null);
  const detailDialog = useRef<HTMLDialogElement>(null);
  const [from, setFrom] = useState(""); const [to, setTo] = useState("");
  const [next, setNext] = useState<number | null>(null); const [offset, setOffset] = useState(0);
  const [wait, setWait] = useState(0); const [loading, setLoading] = useState(false); const [error, setError] = useState("");
  useEffect(() => { if (wait > 0) { const tick = setTimeout(() => setWait(value => value - 1), 1000); return () => clearTimeout(tick); } }, [wait]);
  useEffect(() => {
    const dialog = detailDialog.current;
    if (!dialog) return;
    if (detail) { if (!dialog.open) dialog.showModal(); return; }
    if (dialog.open) dialog.close();
  }, [detail]);
  async function load(start = 0) {
    if (loading || wait > 0) return; setLoading(true); setError("");
    try {
      const query = new URLSearchParams({ offset: String(start) });
      if (from) query.set("from", new Date(`${from}T00:00:00`).toISOString());
      if (to) query.set("to", new Date(`${to}T23:59:59.999`).toISOString());
      const response = await api(`/api/history?${query}`);
      const body = await response.json();
      if (!response.ok) {
        if (response.status >= 500) { setError(withCorrelation(response, "No pudimos consultar los movimientos. Volvé a intentar.")); return; }
        setWait(body.retryAfterSeconds ?? 0);
        setError(response.status === 409 ? "Necesitás vincular Mercado Pago y tener acceso activo para consultar." : "Esperá unos segundos o revisá tu conexión antes de volver a consultar.");
        return;
      }
      setItems(body.items); setNext(body.nextOffset); setOffset(start); setWait(body.buttonCooldownSeconds ?? 10);
    } catch (error) { setError(withCorrelation(error, "No pudimos consultar los movimientos. Volvé a intentar.")); } finally { setLoading(false); }
  }
  function downloadVisible() {
    const cell = (text: string) => `"${(/^\s*[=+@-]/.test(text) ? "'" : "") + text.replaceAll('"', '""')}"`;
    const csv = ["Fecha,Pagador,Monto,Estado,OperacionMP,CoelsaID,Documento", ...items.map(item => [cell(when.format(new Date(item.occurredAt))), cell(item.payerName), item.amount.toFixed(2), cell(item.statusLabel), cell(item.externalId ?? ""), cell(item.coelsaId ?? ""), cell(item.payerDocument ?? "")].join(","))].join("\r\n");
    const url = URL.createObjectURL(new Blob(["\uFEFF", csv], { type: "text/csv;charset=utf-8" }));
    const link = document.createElement("a"); link.href = url; link.download = "movimientos-visibles.csv"; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  return <Shell onSalir={onSalir} back isOwner={session.isOwner}><main className="history">
    <h1>Movimientos de tu cuenta</h1><p>Vista exclusiva del Admin. Consultá los movimientos disponibles en Mercado Pago. Tocá cualquier fila para ver el comprobante completo. La descarga contiene únicamente las filas que ves en esta página.</p>
    <form className="history-range" onSubmit={event => { event.preventDefault(); void load(); }}>
      <label>Desde<input type="date" value={from} onChange={event => { setFrom(event.target.value); setItems([]); setNext(null); setOffset(0); }} /></label>
      <label>Hasta<input type="date" value={to} onChange={event => { setTo(event.target.value); setItems([]); setNext(null); setOffset(0); }} /></label>
      <button type="submit" disabled={loading || wait > 0}>{loading ? "Consultando..." : wait > 0 ? `Esperá ${wait}s...` : "Consultar movimientos"}</button>
      <button type="button" className="secondary" disabled={!items.length} onClick={downloadVisible}>Descargar vista (Excel / CSV)</button>
    </form>
    {error ? <p className="error" role="alert">{error}</p> : null}
    <p className="money">{items.length} movimientos visibles · Acreditado en esta página: {money.format(items.filter(item => item.status === "approved").reduce((total, item) => total + item.amount, 0))}</p>
    <ol className="transfers">{items.map(item => <li key={item.id} className={`row ${item.status}`}><button type="button" className="row-open" onClick={() => setDetail(item)}><div className="when">{when.format(new Date(item.occurredAt))}</div><div className="payer">{item.payerName}</div><div className="amount">{money.format(item.amount)}</div><div className="status">{item.statusLabel}</div></button></li>)}</ol>
    <dialog ref={detailDialog} className="transfer-detail" closedby="any" aria-labelledby="transfer-detail-title" onClose={() => setDetail(null)}>{detail ? <TransferDetailContent detail={detail} /> : null}</dialog>
    <div className="hero-actions"><button className="secondary" disabled={loading || wait > 0 || offset === 0} onClick={() => void load(Math.max(0, offset - 20))}>Página anterior</button><button className="secondary" disabled={loading || wait > 0 || next === null} onClick={() => next !== null && void load(next)}>Página siguiente</button></div>
  </main></Shell>;
}

function Users({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  return (
    <Shell onSalir={onSalir} back isOwner={session.isOwner}>
      <main>
        <h1>Accesos de Mostrador (Puntos de Alerta)</h1>
        <p>Habilitá dispositivos para recibir avisos. Tu plan incluye Puntos de Alerta; cada adicional cuesta $ 1.000 y vence junto con el acceso del Admin.</p>
        {session.isOwner ? <OwnerLogins /> : null}
      </main>
    </Shell>
  );
}

function OwnerLogins() {
  const [rows, setRows] = useState<LoginRow[]>([]);
  const [pointCap, setPointCap] = useState(0);
  const [savingRenewal, setSavingRenewal] = useState<string | null>(null);
  const [alias, setAlias] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [confirming, setConfirming] = useState<string | null>(null);
  const [invite, setInvite] = useState<{ displayName: string; inviteUrl: string; waMeUrl: string } | null>(null);
  const [previewName, setPreviewName] = useState("");

  async function reload() {
    const [logins, aliasResponse, billingResponse] = await Promise.all([api("/api/logins"), api("/api/alias"), api("/api/billing")]);
    if (billingResponse.ok) setPointCap((await billingResponse.json()).alertPointCap);
    if (logins.ok) setRows(await logins.json());
    if (aliasResponse.ok) {
      const body = (await aliasResponse.json()) as { alias?: string | null };
      setAlias(body.alias ?? null);
    }
  }

  useEffect(() => {
    void reload();
  }, []);

  async function confirmAlias(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const response = await api("/api/alias", {
      method: "POST",
      body: JSON.stringify({ alias: data.get("alias") })
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({} as { error?: string }));
      setError(
        body.error === "alias_locked"
          ? "El alias ya está confirmado."
          : body.error === "alias_taken"
            ? "Ese alias ya está en uso."
            : "El alias no sirve."
      );
      return;
    }
    setError("");
    await reload();
  }

  async function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    const response = await api("/api/logins", {
      method: "POST",
      body: JSON.stringify({ name: data.get("name") })
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({} as { error?: string }));
      setError(
        response.status >= 500
          ? withCorrelation(response, "No se pudo crear la cuenta.")
          : body.error === "account_cap"
            ? "No se puede agregar otro Punto de Alerta en este plan."
            : body.error === "alias_required"
              ? "Confirmá el alias primero."
              : "No se pudo crear la cuenta."
      );
      return;
    }
    const body = (await response.json()) as { displayName: string; inviteUrl: string; waMeUrl: string };
    setInvite(body);
    form.reset();
    setPreviewName("");
    setError("");
    await reload();
  }

  async function push(row: LoginRow, approved: boolean) {
    await api(`/api/logins/${row.id}/push`, { method: "PUT", body: JSON.stringify({ approved }) });
    await reload();
  }

  async function revoke(row: LoginRow) {
    // Primer toque pide confirmar; el segundo revoca. Evita revocar por un toque errado.
    if (confirming !== row.id) {
      setConfirming(row.id);
      return;
    }
    setConfirming(null);
    await api(`/api/logins/${row.id}`, { method: "DELETE" });
    await reload();
  }

  async function setRenewal(row: LoginRow, selected: boolean) {
    if (savingRenewal) return;
    setSavingRenewal(row.id); setError("");
    setRows(current => current.map(point => point.id === row.id ? { ...point, renewWithAdmin: selected } : point));
    try {
      const response = await api(`/api/logins/${row.id}/renewal`, { method: "PUT", body: JSON.stringify({ renewWithAdmin: selected }) });
      if (!response.ok) {
        setRows(current => current.map(point => point.id === row.id ? { ...point, renewWithAdmin: row.renewWithAdmin } : point));
        setError(withCorrelation(response, "No pudimos guardar la selección de renovación. Volvé a intentar."));
        return;
      }
      await reload();
    } catch (error) {
      setRows(current => current.map(point => point.id === row.id ? { ...point, renewWithAdmin: row.renewWithAdmin } : point));
      setError(withCorrelation(error, "No pudimos guardar la selección de renovación. Volvé a intentar."));
    } finally { setSavingRenewal(null); }
  }

  const aliasBare = alias?.replace(/^#/, "") ?? "";

  return (
    <section className="logins">
      <p className="point-cap">{rows.filter(row => !row.isOwner && !row.revoked).length} Puntos de Alerta creados · {pointCap} incluidos</p>
      <p>Incluidos: {pointCap}. Los adicionales requieren pago y pueden renovarse con el Admin.</p>
      {alias ? (
        <p className="alias-confirmed">
          Alias confirmado: <strong>{alias}</strong>
        </p>
      ) : (
        <form onSubmit={confirmAlias}>
          <label>
            Alias del comercio *
            <input name="alias" required autoComplete="organization" />
          </label>
          {error ? <p className="error" role="alert">{error}</p> : null}
          <button type="submit">Confirmar alias</button>
        </form>
      )}
      {rows.map((row) => (
        <div className="login-row" key={row.id}>
          <span className="email">{row.displayName ?? row.email}</span>
          <span className="role">{row.isOwner ? "Admin" : "Punto de Alerta"}</span>
          {!row.isOwner && !row.revoked ? <>
            <p>{row.included ? "Incluido en tu plan" : row.enabled ? `Adicional habilitado hasta ${row.extraAccessUntil ? when.format(new Date(row.extraAccessUntil)) : "el vencimiento del Admin"}` : "Adicional pendiente de pago o renovación"}</p>
            {!row.included ? <label><input type="checkbox" checked={row.renewWithAdmin} disabled={savingRenewal !== null} onChange={event => void setRenewal(row, event.target.checked)} />Incluir en la próxima renovación prepaga del Admin (+ $ 1.000)</label> : null}
            {row.requiresPayment ? <button className="secondary" onClick={async () => {
              const response = await api("/api/billing/checkout", { method: "POST", body: JSON.stringify({ additionalPointId: row.id }) });
              if (!response.ok) { setError(response.status >= 500 ? withCorrelation(response, "No pudimos abrir el pago. Volvé a intentar en un momento.") : "Primero necesitás un período activo del Admin para pagar este punto."); return; }
              window.location.assign((await response.json()).initPoint);
            }}>Habilitar adicional por $ 1.000 hasta el vencimiento del Admin</button> : null}
          </> : null}
          {row.revoked ? null : (
            <label>
              <input type="checkbox" checked={row.pushApproved} onChange={(event) => void push(row, event.target.checked)} />
              Habilitar Notificaciones
            </label>
          )}
          {row.isOwner ? null : (
            <button
              className={confirming === row.id ? "danger confirm" : "danger"}
              type="button"
              onClick={() => void revoke(row)}
              onBlur={() => setConfirming((id) => (id === row.id ? null : id))}
              disabled={row.revoked}
            >
              {confirming === row.id ? "Tocá de nuevo para revocar" : "Revocar"}
            </button>
          )}
        </div>
      ))}
      {alias ? (
        <form onSubmit={add}>
          <label>
            Nombre *
            <input
              name="name"
              required
              autoComplete="nickname"
              onChange={(event) => setPreviewName(event.target.value.trim().toLowerCase())}
            />
          </label>
          {previewName ? <p className="invite-preview">{previewName}#{aliasBare}</p> : null}
          {error ? <p className="error" role="alert">{error}</p> : null}
          <button type="submit">Crear invitación</button>
        </form>
      ) : null}
      {invite ? (
        <div className="invite-share">
          <p>
            Enlace para <strong>{invite.displayName}</strong>
          </p>
          <input readOnly value={invite.inviteUrl} aria-label="Enlace de invitación" />
          <a href={invite.waMeUrl} target="_blank" rel="noreferrer">
            Enviar por WhatsApp
          </a>
        </div>
      ) : null}
    </section>
  );
}

type PayPlan = {
  code: string;
  displayName: string;
  amount: number;
  currency: string;
  durationDays?: number | null;
  durationHours?: number | null;
  faceCopy: string;
  enableCopilotHelp: boolean;
};

type PayScreen = {
  accountLabel: string;
  email: string;
  planLabel: string;
  planName?: string | null;
  accessUntil?: string | null;
  plans: PayPlan[];
  blocked: boolean;
  trialEligible: boolean;
  fiveDayWarning: boolean;
  eventReadyCount: number;
  eventDurationHours: number;
  canStartEvent: boolean;
  paymentsNeedingReview: number;
  connected: boolean;
};

function Pay({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  const [screen, setScreen] = useState<PayScreen | null>(null);
  const [myCode, setMyCode] = useState("");
  const [inviteCode, setInviteCode] = useState("");
  const [paying, setPaying] = useState(false);
  const [payError, setPayError] = useState("");

  useEffect(() => {
    api("/api/billing").then(async (response) => {
      if (response.ok) setScreen(await response.json());
    });
    api("/api/referral").then(async (response) => {
      if (!response.ok) return;
      const body = (await response.json()) as { code: string };
      setMyCode(body.code);
    });
  }, []);

  async function startEvent() {
    if (!screen || paying) return;
    const estimate = new Date(Date.now() + screen.eventDurationHours * 3600000);
    if (!window.confirm(`El evento empieza ahora. Las consultas y alertas de TransfersCheck se deshabilitarán el ${when.format(estimate)}. Tu cuenta de Mercado Pago seguirá funcionando. ¿Querés iniciar?`)) return;
    setPaying(true); setPayError("");
    try {
      const response = await api("/api/billing/start-event", { method: "POST" });
      const body = await response.json();
      if (!response.ok) {
        setPayError(response.status >= 500
          ? withCorrelation(response, "No pudimos iniciar el evento. Volvé a intentar en un momento.")
          : "No pudimos iniciar el evento. Verificá que esté pago, vinculado y que no tengas otro período activo.");
        setPaying(false);
        return;
      }
      window.alert(`Evento iniciado. TransfersCheck estará habilitado hasta ${when.format(new Date(body.accessUntil))}.`);
      window.location.assign("/");
    } catch (error) { setPayError(withCorrelation(error, "No pudimos iniciar el evento. Volvé a intentar en un momento.")); setPaying(false); }
  }

  async function pay(planCode: string) {
    if (paying || !session.isOwner) return;
    setPaying(true); setPayError("");
    try {
      const response = await api("/api/billing/checkout", { method: "POST", body: JSON.stringify({ planCode, referralCode: inviteCode || undefined }) });
      if (!response.ok) {
        setPayError(withCorrelation(response, "No pudimos abrir el pago. Volvé a intentar en un momento."));
        setPaying(false);
        return;
      }
      const body = await response.json();
      window.location.assign(body.initPoint);
    } catch (error) { setPayError(withCorrelation(error, "No pudimos abrir el pago. Volvé a intentar en un momento.")); setPaying(false); }
  }

  return (
    <Shell onSalir={onSalir} back isOwner={session.isOwner}>
      {!screen ? <main /> : (
        <>
          <main className="pay">
            {screen.blocked ? <section className="andes-card pause-message" role="alert"><div className="pause-icon">⏸️</div><h1>{screen.accessUntil ? "Las alertas de tu mostrador están en pausa" : "Elegí cómo usar TransfersCheck"}</h1><p>{screen.accessUntil ? "Terminó el período activo de tu cuenta. " : "Activá tu acceso para empezar. "}<strong>Tus Puntos de Alerta configurados se conservan.</strong> Elegí un plan para habilitar la consulta y los avisos de TransfersCheck.</p></section> : <h1>Extendé el acceso</h1>}
            {screen.paymentsNeedingReview > 0 ? <p className="banner warning" role="alert">Recibimos un pago de un Punto de Alerta que no pudo habilitarse porque el período venció o el acceso fue revocado. Ese pago requiere revisión para resolver la habilitación o devolución.</p> : null}
            {screen.fiveDayWarning ? <p className="banner warning">Quedan cinco días de acceso. Pagá para seguir recibiendo avisos.</p> : null}
            <p className="account-row">
              <span className="label">Email</span>
              <span>{screen.email}</span>
            </p>
            <p className="plan-row">
              <span className="label">Plan</span>
              <span>{screen.planName || "Sin plan"}</span>
            </p>
            {myCode ? (
              <p className="referral-code">
                <span className="label">Mi código</span>
                <span>{myCode}</span>
              </p>
            ) : null}
            <label>
              Código de invitación
              <input
                name="referralCode"
                value={inviteCode}
                onChange={(event) => setInviteCode(event.target.value)}
                autoComplete="off"
              />
            </label>
            {screen.eventReadyCount > 0 ? <section className="andes-card"><h2>Tenés {screen.eventReadyCount} pase(s) Individual / Evento disponible(s)</h2><p>El tiempo comienza cuando iniciás el evento. Las alertas y consultas de TransfersCheck vencen {screen.eventDurationHours} horas después.</p>{screen.canStartEvent ? <button disabled={paying} onClick={() => void startEvent()}>Iniciar Evento</button> : <p>{!screen.connected ? "Primero vinculá tu Mercado Pago." : "Podés iniciarlo cuando termine tu período activo."}</p>}</section> : null}
            {payError ? <p className="error" role="alert">{payError}</p> : null}
            {session.isOwner ? <PlanCards primary={!screen.canStartEvent} plans={screen.plans} initial={screen.trialEligible && !screen.blocked} onPay={code => void pay(code)} busy={paying} /> : <p>Pedile al Admin del comercio que renueve el acceso para reactivar las alertas.</p>}
          </main>
        </>
      )}
    </Shell>
  );
}

type HelpCatalog = {
  enableCopilotHelp: boolean;
  helpCenterPath: string;
  menu: { id: string; label: string }[];
  articles: { id: string; title: string; summary: string; path: string }[];
};

type HelpAsk = {
  result: string;
  explanation?: string | null;
  title?: string | null;
  link?: string | null;
  clarification?: string | null;
};

function HelpPage({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  const { id } = useParams();
  const [catalog, setCatalog] = useState<HelpCatalog | null>(null);
  const [article, setArticle] = useState<{ title: string; body: string } | null>(null);
  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState<HelpAsk | null>(null);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState("");
  const [sendingNote, setSendingNote] = useState(false);
  const [noteStatus, setNoteStatus] = useState("");
  const [noteError, setNoteError] = useState("");

  useEffect(() => {
    let gone = false;
    api("/api/help").then(async (response) => {
      if (gone || !response.ok) return;
      setCatalog(await response.json());
    });
    return () => {
      gone = true;
    };
  }, []);

  useEffect(() => {
    if (!id) {
      setArticle(null);
      return;
    }
    let gone = false;
    api(`/api/help/${encodeURIComponent(id)}`).then(async (response) => {
      if (gone || !response.ok) {
        setArticle(null);
        return;
      }
      const body = (await response.json()) as { title: string; body: string };
      setArticle({ title: body.title, body: body.body });
    });
    return () => {
      gone = true;
    };
  }, [id]);

  async function ask(payload: { question?: string; optionId?: string }) {
    setBusy(true);
    setAnswer(null);
    const response = await api("/api/help/ask", {
      method: "POST",
      body: JSON.stringify(payload)
    });
    setBusy(false);
    if (!response.ok) return;
    setAnswer(await response.json());
  }

  async function sendNote(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const text = note.trim();
    if (!text) return;
    setSendingNote(true);
    setNoteStatus("");
    setNoteError("");
    const response = await api("/api/help/messages", {
      method: "POST",
      body: JSON.stringify({ text })
    });
    setSendingNote(false);
    if (!response.ok) {
      setNoteError(response.status >= 500 ? withCorrelation(response, "No se pudo enviar el mensaje.") : "No se pudo enviar el mensaje.");
      return;
    }
    setNote("");
    setNoteStatus("Recibido. Lo lee el dueño del producto en Helpdesk.");
  }

  return (
    <Shell onSalir={onSalir} back isOwner={session.isOwner}>
      <main className="help-page">
        <h1>Ayuda</h1>
        <p className="gate-lead">Orientación sobre el producto. No hay chatbot en Conectar.</p>
        {article ? (
          <article className="help-article">
            <h2>{article.title}</h2>
            <p>{article.body}</p>
            <Link to="/ayuda">Volver al centro de ayuda</Link>
          </article>
        ) : null}
        {!article && catalog ? (
          <>
            <section>
              <h2>Opciones</h2>
              <ul className="help-menu">
                {catalog.menu.map((option) => (
                  <li key={option.id}>
                    <button type="button" disabled={busy} onClick={() => void ask({ optionId: option.id })}>
                      {option.label}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
            {catalog.enableCopilotHelp ? (
              <section>
                <h2>Preguntá</h2>
                <label>
                  Tu pregunta
                  <input
                    value={question}
                    onChange={(event) => setQuestion(event.target.value)}
                    maxLength={500}
                    autoComplete="off"
                  />
                </label>
                <button type="button" disabled={busy || !question.trim()} onClick={() => void ask({ question })}>
                  Enviar
                </button>
              </section>
            ) : (
              <p className="gate-lead">Tu plan usa el menú de opciones. Una pregunta libre no está habilitada.</p>
            )}
            <section>
              <h2>Artículos</h2>
              <ul className="help-articles">
                {catalog.articles.map((item) => (
                  <li key={item.id}>
                    <Link to={item.path}>{item.title}</Link>
                    <span>{item.summary}</span>
                  </li>
                ))}
              </ul>
            </section>
          </>
        ) : null}
        <form className="help-message" onSubmit={(event) => void sendNote(event)}>
          <h2>Escribile al producto</h2>
          <label htmlFor="help-message">Mensaje</label>
          <textarea
            id="help-message"
            name="text"
            value={note}
            onChange={(event) => {
              setNote(event.target.value);
              if (noteError) setNoteError("");
            }}
            maxLength={1000}
            required
            rows={4}
            aria-describedby="help-message-hint"
          />
          <p id="help-message-hint" className="gate-lead">Un mensaje corto. No es un chat.</p>
          {noteError ? <p className="error" role="alert">{noteError}</p> : null}
          {noteStatus ? <p role="status">{noteStatus}</p> : null}
          <button type="submit" disabled={sendingNote}>Enviar mensaje</button>
        </form>
        {answer ? (
          <section className="help-answer" aria-live="polite">
            {answer.explanation ? <p>{answer.explanation}</p> : null}
            {answer.clarification ? <p>{answer.clarification}</p> : null}
            {answer.title && answer.link ? (
              <p>
                <Link to={answer.link}>{answer.title}</Link>
              </p>
            ) : answer.link ? (
              <p>
                <Link to={answer.link}>Centro de ayuda</Link>
              </p>
            ) : null}
          </section>
        ) : null}
      </main>
    </Shell>
  );
}
