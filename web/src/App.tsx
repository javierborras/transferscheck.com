import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Link, Route, Routes, useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { api, apiUrl } from "./api";
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
  videoUrl: string;
};
type Transfer = {
  id: string;
  payerName: string;
  amount: number;
  occurredAt: string;
  status: string;
  statusLabel: string;
};
type LoginRow = {
  id: string;
  email: string;
  displayName?: string;
  isOwner: boolean;
  pushApproved: boolean;
  revoked: boolean;
  passwordSet?: boolean;
};

let firstOpen: Promise<void> | null = null;
const money = new Intl.NumberFormat("es-AR", { style: "currency", currency: "ARS", maximumFractionDigits: 0 });
const when = new Intl.DateTimeFormat("es-AR", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false
});

function beep() {
  const context = new AudioContext();
  const tone = context.createOscillator();
  const gain = context.createGain();
  tone.type = "sine";
  tone.frequency.value = 1000;
  gain.gain.value = 0.08;
  tone.connect(gain);
  gain.connect(context.destination);
  tone.start();
  tone.stop(context.currentTime + 0.2);
  tone.onended = () => void context.close();
}

function Mark() {
  return (
    <svg className="mark" viewBox="0 0 120 120" fill="none" aria-hidden="true">
      <rect width="120" height="120" rx="28" fill="#009EE3" />
      <circle cx="60" cy="60" r="44" stroke="#FFFFFF" strokeOpacity="0.25" strokeWidth="4" strokeDasharray="4 4" />
      <path d="M38 60C38 47.8497 47.8497 38 60 38C69.317 38 77.2952 43.7841 80.5 52" stroke="#FFFFFF" strokeWidth="5" strokeLinecap="round" />
      <path d="M82 46V53H75" stroke="#FFFFFF" strokeWidth="5" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M82 60C82 72.1503 72.1503 82 60 82C50.683 82 42.7048 76.2159 39.5 68" stroke="#FFFFFF" strokeWidth="5" strokeLinecap="round" />
      <path d="M38 74V67H45" stroke="#FFFFFF" strokeWidth="5" strokeLinecap="round" strokeLinejoin="round" />
      <circle cx="60" cy="60" r="20" fill="#FFFFFF" />
      <path d="M52 60.5L57.5 66L68.5 54" stroke="#00A650" strokeWidth="4.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function Wordmark() {
  return (
    <span className="wordmark">
      <Mark />
      <span className="name">TransfersCheck</span>
    </span>
  );
}

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
        <Mark />
        <span className="name">TransfersCheck</span>
      </Link>
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
              Transacciones
            </Link>
            <Link to="/ayuda" aria-current={here("/ayuda")}>
              Ayuda
            </Link>
            {isOwner ? (
              <>
                <Link to="/historial" aria-current={here("/historial")}>
                  Historial
                </Link>
                <Link to="/usuarios" aria-current={here("/usuarios")}>
                  Configurar
                </Link>
                <Link to="/pagar" aria-current={here("/pagar")}>
                  Pagos
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
      {children}
    </div>
  );
}

export function App() {
  const navigate = useNavigate();
  const [session, setSession] = useState<Session | null | undefined>(undefined);

  useEffect(() => {
    let gone = false;
    api("/api/session").then(async (response) => {
      if (gone) return;
      setSession(response.ok ? await response.json() : null);
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
      <Route path="/cuenta" element={<CreateAccount onSignedIn={setSession} />} />
      <Route path="/invitar/:token" element={<ClaimInvite onSignedIn={setSession} />} />
      <Route path="/owner" element={<OwnerSite />} />
      <Route path="/pagar" element={session ? <Pay session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/conectar" element={session ? <ChecklistPage session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/usuarios" element={session ? <Users session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/ayuda/:id?" element={session ? <HelpPage session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/historial" element={session?.isOwner ? <HistoryPage session={session} onSalir={salir} /> : session ? <Transfers session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="*" element={session ? <Transfers session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
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
  window.location.assign(apiUrl(`/api/auth0/login${signUp ? "?signup=true" : ""}`));
}

function auth0Message(code: string | null) {
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
      return "No se pudo iniciar sesión con Auth0.";
  }
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
      setError("No se pudo crear la cuenta.");
      return;
    }
    const current = await api("/api/session");
    if (!current.ok) {
      setError("No se pudo crear la cuenta.");
      return;
    }
    onSignedIn(await current.json());
    navigate("/");
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
          Crear cuenta con Auth0
        </button>
        <p className="switch">
          <Link to="/">Ya tengo cuenta</Link>
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
      setError("No se pudo abrir la sesión.");
      return;
    }
    onSignedIn(await current.json());
    navigate("/");
  }

  if (auth0 === undefined) {
    return (
      <main className="gate">
        <Wordmark />
      </main>
    );
  }

  const adminAuth0 = auth0 && !alertPoint;
  const auth0Error = auth0Message(params.get("auth0_error"));

  return (
    <main className="gate">
      <Wordmark />
      <h1>Hola de nuevo</h1>
      <p className="gate-lead">Inicia sesión en tu cuenta de TransfersCheck</p>
      {adminAuth0 ? (
        <>
          {auth0Error ? <p className="error">{auth0Error}</p> : null}
          <button type="button" onClick={() => startAuth0(false)}>
            Iniciar sesión con Auth0
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
      setError("El enlace no sirve o ya se usó.");
      return;
    }
    const current = await api("/api/session");
    if (!current.ok) {
      setError("El enlace no sirve o ya se usó.");
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

function helpLines(help: string) {
  const lines = help.split(/(?<=\.)\s+(?=\d+\.)/).map((line) => line.trim()).filter(Boolean);
  return lines.length > 1 ? lines : [help];
}

function ChecklistPage({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  const [checklist, setChecklist] = useState<Checklist | null>(null);

  async function reload() {
    const response = await api("/api/checklist");
    if (response.ok) setChecklist(await response.json());
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
      setError("No se pudo empezar la conexión.");
      return;
    }
    const body = await response.json();
    window.location.assign(body.authorizationUrl);
  }

  async function enableAlerts() {
    if ("Notification" in window && Notification.permission === "default") {
      await Promise.race([
        Notification.requestPermission(),
        new Promise((resolve) => setTimeout(resolve, 1500))
      ]);
    }
    const config = await api("/api/push/config");
    const keys = config.ok ? await config.json() : {};
    const token = keys.apiKey ? "" : `mock-${crypto.randomUUID()}`;
    if (!token) return;
    const saved = await api("/api/push/tokens", { method: "POST", body: JSON.stringify({ token }) });
    if (saved.ok) {
      await onReload();
      navigate("/");
    }
  }

  const connectBody = (
    <>
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
        <p className="connection-badge" data-connected={checklist.connectDone ? "yes" : "no"} role="status">
          {checklist.connectionBadge}
        </p>
        <ol className="steps">
          <li className={checklist.accountDone ? "done" : "current"} data-step="1">
            <div className="step-body">
              {checklist.accountDone ? checklist.accountTitle : <h1>{checklist.accountTitle}</h1>}
            </div>
          </li>
          <li className={connectNow ? "current" : checklist.connectDone ? "done" : "later"} data-step="2">
            <div className="step-body">
              {connectNow ? <h1>{checklist.connectTitle}</h1> : checklist.connectTitle}
              {connectNow || checklist.connectDone ? connectBody : null}
            </div>
          </li>
          <li className={alertsNow ? "current" : checklist.alertsDone ? "done" : "later"} data-step="3">
            <div className="step-body">
              {alertsNow ? <h1>{checklist.alertsTitle}</h1> : checklist.alertsTitle}
              <p className="ios-hint">{checklist.iosHint}</p>
            </div>
          </li>
          <li className={checklist.trialRunning ? "done" : "later"} data-step="4" data-status="trial">
            <div className="step-body" role="status">
              {checklist.trialLine}
            </div>
          </li>
        </ol>
      </main>
      {connectNow ? (
        <Dock>
          <button type="button" onClick={() => void connect()}>
            {checklist.button}
          </button>
        </Dock>
      ) : null}
      {checklist.connectDone && !alertsNow ? (
        <Dock>
          <button type="button" onClick={() => void connect()}>
            {checklist.reconnectButton}
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
  const [alertsDone, setAlertsDone] = useState<boolean | null>(null);
  const [trial, setTrial] = useState(false);
  const [expired, setExpired] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [fiveDayWarning, setFiveDayWarning] = useState(false);
  const [consultaWarn, setConsultaWarn] = useState(false);
  const [pollSeconds, setPollSeconds] = useState(300);
  const [cooldownSeconds, setCooldownSeconds] = useState(60);
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
    const response = await api(path);
    if (!response.ok) return;
    const body = await response.json();
    const next = body.items as Transfer[];
    setPollSeconds(body.pollIntervalSeconds);
    setCooldownSeconds(body.buttonCooldownSeconds);
    if (announce) announceArrivals(next);
    else seen.current = new Map(next.map((item) => [item.id, item.status]));
    setItems(next);
    const check = await api("/api/checklist");
    if (check.ok) {
      const state = await check.json();
      setAlertsDone(state.alertsDone);
      setTrial(state.trialRunning);
    }
    const billing = await api("/api/billing");
    if (billing.ok) {
      const { accessUntil, fiveDayWarning, blocked: isBlocked, consultaWarn: warnConsultas } = await billing.json();
      setExpired(Boolean(accessUntil) && new Date(accessUntil).getTime() <= Date.now());
      setBlocked(Boolean(isBlocked));
      setFiveDayWarning(Boolean(fiveDayWarning));
      setConsultaWarn(Boolean(warnConsultas));
    }
  }

  function announceArrivals(next: Transfer[]) {
    const previous = seen.current;
    seen.current = new Map(next.map((item) => [item.id, item.status]));
    if (!previous) return;
    const arrived = next.find((item) => item.status === "approved" && previous.get(item.id) !== "approved");
    if (!arrived) return;
    beep();
    setNotice(arrived);
  }

  useEffect(() => {
    let gone = false;
    const opening = session.isOwner
      ? (firstOpen ??= api("/api/transfers/refresh", { method: "POST" }).then(() => undefined, () => undefined))
      : Promise.resolve();
    void opening.then(() => {
      if (!gone) void load(false);
    });
    const timer = window.setInterval(() => {
      if (!gone) void load(true);
    }, pollSeconds * 1000);
    const origin = import.meta.env.VITE_API_ORIGIN ?? "http://localhost:5080";
    const source = new EventSource(`${origin}/api/transfers/live`, { withCredentials: true });
    source.addEventListener(liveTransferMessageType, (event) => {
      if (gone) return;
      const message = JSON.parse((event as MessageEvent).data) as LiveTransferArrived;
      if (message.messageType !== liveTransferMessageType) return;
      setItems((current) => {
        if (current.some((item) => item.id === message.id)) return current;
        const row: Transfer = {
          id: message.id,
          payerName: message.payerName,
          amount: message.amount,
          occurredAt: message.occurredAt,
          status: message.status,
          statusLabel: message.statusLabel
        };
        return [row, ...current].slice(0, current.length || 10);
      });
    });
    return () => {
      gone = true;
      window.clearInterval(timer);
      source.close();
    };
  }, [first, pollSeconds, session.isOwner]);

  useEffect(() => {
    if (cooldown <= 0) return;
    const tick = window.setTimeout(() => setCooldown((value) => value - 1), 1000);
    return () => window.clearTimeout(tick);
  }, [cooldown]);

  async function refresh() {
    if (!session.isOwner || cooldown > 0 || loading) return;
    setLoading(true);
    setWait("");
    const response = await api("/api/transfers/refresh", { method: "POST" });
    if (response.status === 429) {
      const body = await response.json().catch(() => ({} as { error?: string }));
      setWait(
        body.error === "quota_wait"
          ? "Llegaste al tope de consultas de esta semana. Esperá al próximo período."
          : "Mercado Pago está demorado. Aguardá unos instantes para volver a consultar."
      );
    } else if (response.status === 409) setExpired(true);
    else await load(true);
    setLoading(false);
    setCooldown(cooldownSeconds);
  }

  async function openDetail(id: string) {
    const response = await api(`/api/transfers/${id}`);
    if (response.ok) setDetail(await response.json());
  }

  async function enableAlerts() {
    if ("Notification" in window && Notification.permission === "default") {
      await Promise.race([
        Notification.requestPermission(),
        new Promise((resolve) => setTimeout(resolve, 1500))
      ]);
    }
    const config = await api("/api/push/config");
    const keys = config.ok ? await config.json() : {};
    const token = keys.apiKey ? "" : `mock-${crypto.randomUUID()}`;
    if (!token) return;
    const saved = await api("/api/push/tokens", { method: "POST", body: JSON.stringify({ token }) });
    if (saved.ok) setAlertsDone(true);
  }

  const cooling = cooldown > 0;

  return (
    <Shell onSalir={onSalir} counter isOwner={session.isOwner}>
      <main className="board">
        {blocked || expired ? (
          <section className="access-block" role="alert">
            <h1>Se terminó el acceso</h1>
            <p>Para seguir viendo transferencias y avisos, extendé el acceso.</p>
            {session.isOwner ? <Link to="/pagar">Ir a Pagos</Link> : null}
          </section>
        ) : (
          <>
            <div className="board-scroll">
              {session.isOwner && fiveDayWarning ? (
                <p className="banner warning" role="status">
                  <span>Quedan cinco días de acceso. Pagá para seguir recibiendo avisos.</span>
                  <Link to="/pagar">Ir a Pagos</Link>
                </p>
              ) : session.isOwner && trial ? (
                <p className="banner trial">La prueba está en curso.</p>
              ) : null}
              {session.isOwner && consultaWarn ? (
                <p className="banner warning" role="status">
                  Te quedan pocas consultas de esta semana.
                </p>
              ) : null}
              {notice ? (
                <p className="notice" role="status">
                  <strong>¡Entró un pago!</strong>
                  <span>{notice.payerName} te transfirió {money.format(notice.amount)}</span>
                </p>
              ) : null}
              {alertsDone === false ? (
                <section className="alerts">
                  <h2>Activá los avisos</h2>
                  <p>En el iPhone, agregá la app a la pantalla de inicio.</p>
                  <button type="button" onClick={enableAlerts}>Activar avisos</button>
                </section>
              ) : null}
              <div className="transfer-columns" role="row">
                <span>Fecha</span>
                <span>Pagador</span>
                <span>Monto</span>
                <span>Estado</span>
              </div>
              <ol className="transfers">
                {items.map((item) => {
                  const tone = item.status === "approved" || item.status === "pending" || item.status === "rejected" ? item.status : "other";
                  return (
                    <li key={item.id} className={first === item.id ? `row ${tone} focus` : `row ${tone}`}>
                      <button type="button" className="row-open" onClick={() => void openDetail(item.id)}>
                        <div className="when">{when.format(new Date(item.occurredAt))}</div>
                        <div className="payer">{item.payerName || "Desconocido"}</div>
                        <div className="amount">{money.format(item.amount)}</div>
                        <div className="status">{item.statusLabel}</div>
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
              {detail ? (
                <>
                  <h2 id="transfer-detail-title">Detalle</h2>
                  <p>{when.format(new Date(detail.occurredAt))}</p>
                  <p>{detail.payerName || "Desconocido"}</p>
                  <p>{money.format(detail.amount)}</p>
                  <p>{detail.statusLabel}</p>
                  <form method="dialog">
                    <button type="submit">Cerrar</button>
                  </form>
                </>
              ) : null}
            </dialog>
            {wait ? <p className="wait" role="status">{wait}</p> : null}
            {session.isOwner ? (
              <Dock>
                <button
                  className={cooling ? "cooldown" : undefined}
                  type="button"
                  onClick={refresh}
                  aria-disabled={cooling || loading}
                  disabled={loading}
                >
                  {cooling ? `Esperá ${cooldown}s...` : loading ? "Actualizando..." : "Actualizar ahora"}
                </button>
              </Dock>
            ) : null}
          </>
        )}
      </main>
    </Shell>
  );
}

function HistoryPage({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  const [items, setItems] = useState<Transfer[]>([]);
  const [totals, setTotals] = useState<{ byDay: number; byMonth: number; byRange: number } | null>(null);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  async function load() {
    const query = new URLSearchParams();
    if (from) query.set("from", new Date(from).toISOString());
    if (to) query.set("to", new Date(to).toISOString());
    const suffix = query.toString() ? `?${query}` : "";
    const response = await api(`/api/history${suffix}`);
    if (!response.ok) return;
    const body = await response.json();
    setItems(body.items);
    setTotals(body.totals);
  }

  useEffect(() => {
    void load();
  }, []);

  return (
    <Shell onSalir={onSalir} back isOwner={session.isOwner}>
      <main className="history">
        <h1>Historial de Transacciones</h1>
        <form
          className="history-range"
          onSubmit={(event) => {
            event.preventDefault();
            void load();
          }}
        >
          <label>
            Desde
            <input type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
          </label>
          <label>
            Hasta
            <input type="date" value={to} onChange={(event) => setTo(event.target.value)} />
          </label>
          <button type="submit">Consultar</button>
          <button
            type="button"
            onClick={() => {
              void (async () => {
                const query = new URLSearchParams();
                if (from) query.set("from", new Date(from).toISOString());
                if (to) query.set("to", new Date(to).toISOString());
                const suffix = query.toString() ? `?${query}` : "";
                const response = await api(`/api/history/csv${suffix}`);
                if (!response.ok) return;
                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement("a");
                link.href = url;
                link.download = "historial.csv";
                link.click();
                URL.revokeObjectURL(url);
              })();
            }}
          >
            CSV
          </button>
        </form>
        {totals ? (
          <p className="history-totals">
            Día {money.format(totals.byDay)} · Mes {money.format(totals.byMonth)} · Rango {money.format(totals.byRange)}
          </p>
        ) : null}
        <div className="transfer-columns" role="row">
          <span>Fecha</span>
          <span>Pagador</span>
          <span>Monto</span>
          <span>Estado</span>
        </div>
        <ol className="transfers">
          {items.map((item) => (
            <li key={item.id} className="row">
              <div className="when">{when.format(new Date(item.occurredAt))}</div>
              <div className="payer">{item.payerName || "Desconocido"}</div>
              <div className="amount">{money.format(item.amount)}</div>
              <div className="status">{item.statusLabel}</div>
            </li>
          ))}
        </ol>
      </main>
    </Shell>
  );
}

function Users({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  return (
    <Shell onSalir={onSalir} back isOwner={session.isOwner}>
      <main>
        <h1>Configurar</h1>
        {session.isOwner ? <OwnerLogins /> : null}
      </main>
    </Shell>
  );
}

function OwnerLogins() {
  const [rows, setRows] = useState<LoginRow[]>([]);
  const [alias, setAlias] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [confirming, setConfirming] = useState<string | null>(null);
  const [invite, setInvite] = useState<{ displayName: string; inviteUrl: string; waMeUrl: string } | null>(null);
  const [previewName, setPreviewName] = useState("");

  async function reload() {
    const [logins, aliasResponse] = await Promise.all([api("/api/logins"), api("/api/alias")]);
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
    const data = new FormData(event.currentTarget);
    const response = await api("/api/logins", {
      method: "POST",
      body: JSON.stringify({ name: data.get("name") })
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({} as { error?: string }));
      setError(
        body.error === "account_cap"
          ? "No se puede agregar otro Punto de Alerta en este plan."
          : body.error === "alias_required"
            ? "Confirmá el alias primero."
            : "No se pudo crear la cuenta."
      );
      return;
    }
    const body = (await response.json()) as { displayName: string; inviteUrl: string; waMeUrl: string };
    setInvite(body);
    event.currentTarget.reset();
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

  const aliasBare = alias?.replace(/^#/, "") ?? "";

  return (
    <section className="logins">
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
};

function Pay({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  const [screen, setScreen] = useState<PayScreen | null>(null);
  const [myCode, setMyCode] = useState("");
  const [inviteCode, setInviteCode] = useState("");

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

  async function pay() {
    const response = await api("/api/billing/checkout", {
      method: "POST",
      body: JSON.stringify({ referralCode: inviteCode || undefined })
    });
    if (!response.ok) return;
    const body = await response.json();
    window.location.assign(body.initPoint);
  }

  return (
    <Shell onSalir={onSalir} back isOwner={session.isOwner}>
      {!screen ? <main /> : (
        <>
          <main className="pay">
            <h1>Extendé el acceso</h1>
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
            <ul className="plans">
              {screen.plans.map((plan) => (
                <li key={plan.code} className="plan">
                  <strong>{plan.displayName}</strong>
                  <span className="amount">{money.format(plan.amount)}</span>
                  <span className="period">
                    {plan.durationHours
                      ? `${plan.durationHours} horas`
                      : plan.durationDays
                        ? `${plan.durationDays} días`
                        : ""}
                  </span>
                  {plan.faceCopy ? <span className="face">{plan.faceCopy}</span> : null}
                </li>
              ))}
            </ul>
          </main>
          <Dock>
            <button type="button" onClick={pay}>Pagar con Mercado Pago</button>
          </Dock>
        </>
      )}
    </Shell>
  );
}

type OwnerCard = { card: string; isSet: boolean; lastFour: string | null; expiresAt: string | null };
type OwnerPlan = {
  code: string;
  displayName: string;
  amount: number;
  currency: string;
  durationDays?: number | null;
  durationHours?: number | null;
  faceCopy: string;
  enableCopilotHelp: boolean;
  active: boolean;
};

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

const ownerCardLabels: Record<string, string> = {
  mercadopago_platform: "Mercado Pago (plataforma)",
  mail: "Mail",
  browser_push: "Notificaciones web",
  whatsapp: "WhatsApp"
};

function HelpPage({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  const { id } = useParams();
  const [catalog, setCatalog] = useState<HelpCatalog | null>(null);
  const [article, setArticle] = useState<{ title: string; body: string } | null>(null);
  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState<HelpAsk | null>(null);
  const [busy, setBusy] = useState(false);

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

function OwnerSite() {
  const [email, setEmail] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [cards, setCards] = useState<OwnerCard[]>([]);
  const [plans, setPlans] = useState<OwnerPlan[]>([]);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [planDrafts, setPlanDrafts] = useState<Record<string, OwnerPlan>>({});
  const [newPlan, setNewPlan] = useState({ code: "", displayName: "", amount: "", faceCopy: "", enableCopilotHelp: false });

  async function loadPanel() {
    const [services, planRows] = await Promise.all([api("/api/owner/services"), api("/api/owner/plans")]);
    if (!services.ok || !planRows.ok) {
      setEmail(null);
      return;
    }
    const nextPlans = (await planRows.json()) as OwnerPlan[];
    setCards(await services.json());
    setPlans(nextPlans);
    setPlanDrafts(Object.fromEntries(nextPlans.map((plan) => [plan.code, { ...plan }])));
  }

  useEffect(() => {
    let gone = false;
    api("/api/owner/session").then(async (response) => {
      if (gone) return;
      if (!response.ok) {
        setEmail(null);
        return;
      }
      const body = (await response.json()) as { email: string };
      setEmail(body.email);
      await loadPanel();
    });
    return () => {
      gone = true;
    };
  }, []);

  async function signIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const response = await api("/api/owner/sessions", {
      method: "POST",
      body: JSON.stringify({ email: data.get("email"), password: data.get("password") })
    });
    if (!response.ok) {
      setError("El correo o la contraseña no sirven.");
      return;
    }
    const body = (await response.json()) as { email: string };
    setEmail(body.email);
    setError("");
    await loadPanel();
  }

  async function signOut() {
    await api("/api/owner/sessions", { method: "DELETE" });
    setEmail(null);
    setCards([]);
    setPlans([]);
  }

  async function replaceSecret(card: string) {
    const secret = drafts[card]?.trim() ?? "";
    if (!secret) {
      setError("Pegá el secreto de nuevo para reemplazarlo.");
      return;
    }
    const response = await api(`/api/owner/services/${card}`, {
      method: "PUT",
      body: JSON.stringify({ secret })
    });
    if (!response.ok) {
      setError("No se pudo guardar el secreto.");
      return;
    }
    setDrafts((value) => ({ ...value, [card]: "" }));
    setError("");
    await loadPanel();
  }

  async function savePlan(code: string) {
    const draft = planDrafts[code];
    if (!draft) return;
    const response = await api(`/api/owner/plans/${encodeURIComponent(code)}`, {
      method: "PUT",
      body: JSON.stringify(draft)
    });
    if (!response.ok) {
      setError("No se pudo guardar el plan.");
      return;
    }
    setError("");
    await loadPanel();
  }

  async function createPlan(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const amount = Number(newPlan.amount);
    if (!newPlan.code.trim() || !newPlan.displayName.trim() || !Number.isFinite(amount)) {
      setError("Código, nombre e importe son obligatorios.");
      return;
    }
    const response = await api("/api/owner/plans", {
      method: "POST",
      body: JSON.stringify({
        code: newPlan.code.trim(),
        displayName: newPlan.displayName.trim(),
        amount,
        faceCopy: newPlan.faceCopy,
        enableCopilotHelp: newPlan.enableCopilotHelp,
        active: true,
        currency: "ARS"
      })
    });
    if (!response.ok) {
      setError("No se pudo crear el plan.");
      return;
    }
    setNewPlan({ code: "", displayName: "", amount: "", faceCopy: "", enableCopilotHelp: false });
    setError("");
    await loadPanel();
  }

  if (!email) {
    return (
      <main className="gate">
        <Wordmark />
        <h1>Owner</h1>
        <p className="gate-lead">Acceso del producto. No es la puerta del comercio.</p>
        <form onSubmit={signIn}>
          <label>
            Dirección de email *
            <input name="email" type="email" autoComplete="username" required />
          </label>
          <label>
            Contraseña *
            <input name="password" type="password" autoComplete="current-password" required />
          </label>
          {error ? <p className="error">{error}</p> : null}
          <button type="submit">Entrar</button>
        </form>
      </main>
    );
  }

  return (
    <main className="shell page">
      <header className="app-header">
        <Wordmark />
        <span className="alias-slot">{email}</span>
        <button className="salir" type="button" onClick={() => void signOut()}>
          Salir
        </button>
      </header>
      {error ? <p className="error">{error}</p> : null}

      <h1>Planes</h1>
      <p className="gate-lead">Editá precios y alta de planes. La pantalla de Pagos del comercio solo lee los activos.</p>
      <ul className="plans">
        {plans.map((plan) => {
          const draft = planDrafts[plan.code] ?? plan;
          return (
            <li key={plan.code} className="plan">
              <strong>{plan.code}</strong>
              <label>
                Nombre
                <input
                  value={draft.displayName}
                  onChange={(event) =>
                    setPlanDrafts((value) => ({ ...value, [plan.code]: { ...draft, displayName: event.target.value } }))
                  }
                />
              </label>
              <label>
                Importe
                <input
                  type="number"
                  value={draft.amount}
                  onChange={(event) =>
                    setPlanDrafts((value) => ({
                      ...value,
                      [plan.code]: { ...draft, amount: Number(event.target.value) }
                    }))
                  }
                />
              </label>
              <label>
                Texto de cara
                <input
                  value={draft.faceCopy}
                  onChange={(event) =>
                    setPlanDrafts((value) => ({ ...value, [plan.code]: { ...draft, faceCopy: event.target.value } }))
                  }
                />
              </label>
              <label className="check">
                <input
                  type="checkbox"
                  checked={draft.enableCopilotHelp}
                  onChange={(event) =>
                    setPlanDrafts((value) => ({
                      ...value,
                      [plan.code]: { ...draft, enableCopilotHelp: event.target.checked }
                    }))
                  }
                />
                EnableCopilotHelp
              </label>
              <label className="check">
                <input
                  type="checkbox"
                  checked={draft.active}
                  onChange={(event) =>
                    setPlanDrafts((value) => ({ ...value, [plan.code]: { ...draft, active: event.target.checked } }))
                  }
                />
                Activo
              </label>
              <button type="button" onClick={() => void savePlan(plan.code)}>
                Guardar plan
              </button>
            </li>
          );
        })}
      </ul>
      <form className="owner-new-plan" onSubmit={createPlan}>
        <h2>Nuevo plan</h2>
        <label>
          Código
          <input value={newPlan.code} onChange={(event) => setNewPlan({ ...newPlan, code: event.target.value })} required />
        </label>
        <label>
          Nombre
          <input
            value={newPlan.displayName}
            onChange={(event) => setNewPlan({ ...newPlan, displayName: event.target.value })}
            required
          />
        </label>
        <label>
          Importe
          <input
            type="number"
            value={newPlan.amount}
            onChange={(event) => setNewPlan({ ...newPlan, amount: event.target.value })}
            required
          />
        </label>
        <label className="check">
          <input
            type="checkbox"
            checked={newPlan.enableCopilotHelp}
            onChange={(event) => setNewPlan({ ...newPlan, enableCopilotHelp: event.target.checked })}
          />
          EnableCopilotHelp
        </label>
        <button type="submit">Agregar plan</button>
      </form>

      <h1>Panel de servicios</h1>
      <p className="gate-lead">Cada tarjeta es un servicio que el producto ya usa. El valor completo no se muestra.</p>
      <ul className="plans">
        {cards.map((card) => (
          <li key={card.card} className="plan">
            <strong>{ownerCardLabels[card.card] ?? card.card}</strong>
            <span>
              {card.isSet
                ? card.lastFour
                  ? `Configurado · ···${card.lastFour}`
                  : "Configurado"
                : "Sin configurar"}
            </span>
            {card.card !== "whatsapp" ? (
              <>
                <label>
                  Reemplazar secreto
                  <input
                    type="password"
                    autoComplete="off"
                    value={drafts[card.card] ?? ""}
                    onChange={(event) => setDrafts((value) => ({ ...value, [card.card]: event.target.value }))}
                  />
                </label>
                <button type="button" onClick={() => void replaceSecret(card.card)}>
                  Guardar
                </button>
              </>
            ) : (
              <span className="face">Solo wa.me · sin clave</span>
            )}
          </li>
        ))}
      </ul>
    </main>
  );
}
