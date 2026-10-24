import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Link, Route, Routes, useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { api } from "./api";

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
type LoginRow = { id: string; email: string; isOwner: boolean; pushApproved: boolean; revoked: boolean };

let firstOpen: Promise<void> | null = null;
const money = new Intl.NumberFormat("es-AR", { style: "currency", currency: "ARS", maximumFractionDigits: 0 });
const clock = new Intl.DateTimeFormat("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });
const when = new Intl.DateTimeFormat("es-AR", { dateStyle: "short", timeStyle: "short" });

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

function AppHeader({ onSalir, back }: { onSalir: () => Promise<void>; back: boolean }) {
  const [open, setOpen] = useState(false);
  const location = useLocation();

  useEffect(() => {
    setOpen(false);
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
      <span id="alias" className="alias-slot" aria-label="Alias" />
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
            <Link to="/usuarios" aria-current={here("/usuarios")}>
              Configurar
            </Link>
            <Link to="/pagar" aria-current={here("/pagar")}>
              Pagos
            </Link>
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
  children
}: {
  onSalir: () => Promise<void>;
  back?: boolean;
  counter?: boolean;
  children: ReactNode;
}) {
  return (
    <div className={counter ? "shell counter" : "shell page"}>
      <AppHeader onSalir={onSalir} back={back ?? false} />
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
      <Route path="/pagar" element={session ? <Pay onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/conectar" element={session ? <ChecklistPage onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="/usuarios" element={session ? <Users session={session} onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
      <Route path="*" element={session ? <Transfers onSalir={salir} /> : <Enter onSignedIn={setSession} />} />
    </Routes>
  );
}

function CreateAccount({ onSignedIn }: { onSignedIn: (session: Session) => void }) {
  const navigate = useNavigate();
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
  const [error, setError] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const response = await api("/api/sessions", {
      method: "POST",
      body: JSON.stringify({ email: data.get("email"), password: data.get("password") })
    });
    if (!response.ok) {
      setError("El correo o la contraseña no sirven.");
      return;
    }
    const current = await api("/api/session");
    if (!current.ok) {
      setError("El correo o la contraseña no sirven.");
      return;
    }
    onSignedIn(await current.json());
    navigate("/");
  }

  return (
    <main className="gate">
      <Wordmark />
      <h1>Hola de nuevo</h1>
      <p className="gate-lead">Inicia sesión en tu cuenta de TransfersCheck</p>
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
        {error ? <p className="error">{error}</p> : null}
        <button type="submit">Iniciar sesión</button>
      </form>
      <p className="switch">
        <Link to="/cuenta">Creá tu cuenta</Link>
      </p>
    </main>
  );
}

function helpLines(help: string) {
  const lines = help.split(/(?<=\.)\s+(?=\d+\.)/).map((line) => line.trim()).filter(Boolean);
  return lines.length > 1 ? lines : [help];
}

function ChecklistPage({ onSalir }: { onSalir: () => Promise<void> }) {
  const [checklist, setChecklist] = useState<Checklist | null>(null);

  async function reload() {
    const response = await api("/api/checklist");
    if (response.ok) setChecklist(await response.json());
  }

  useEffect(() => {
    void reload();
  }, []);

  return (
    <Shell onSalir={onSalir} back>
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

function Transfers({ onSalir }: { onSalir: () => Promise<void> }) {
  const [params] = useSearchParams();
  const [items, setItems] = useState<Transfer[]>([]);
  const [notice, setNotice] = useState<Transfer | null>(null);
  const [wait, setWait] = useState("");
  const [cooldown, setCooldown] = useState(0);
  const [loading, setLoading] = useState(false);
  const [alertsDone, setAlertsDone] = useState<boolean | null>(null);
  const [trial, setTrial] = useState(false);
  const [expired, setExpired] = useState(false);
  const [pollSeconds, setPollSeconds] = useState(300);
  const [cooldownSeconds, setCooldownSeconds] = useState(60);
  const seen = useRef<Map<string, string> | null>(null);
  const first = params.get("first");

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
      const { accessUntil } = await billing.json();
      setExpired(Boolean(accessUntil) && new Date(accessUntil).getTime() < Date.now());
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
    const opening = (firstOpen ??= api("/api/transfers/refresh", { method: "POST" }).then(() => undefined, () => undefined));
    void opening.then(() => {
      if (!gone) void load(false);
    });
    const timer = window.setInterval(() => {
      if (!gone) void load(true);
    }, pollSeconds * 1000);
    return () => {
      gone = true;
      window.clearInterval(timer);
    };
  }, [first, pollSeconds]);

  useEffect(() => {
    if (cooldown <= 0) return;
    const tick = window.setTimeout(() => setCooldown((value) => value - 1), 1000);
    return () => window.clearTimeout(tick);
  }, [cooldown]);

  async function refresh() {
    if (cooldown > 0 || loading) return;
    setLoading(true);
    setWait("");
    const response = await api("/api/transfers/refresh", { method: "POST" });
    if (response.status === 429) setWait("Mercado Pago está demorado. Aguardá unos instantes para volver a consultar.");
    else if (response.status === 409) setExpired(true);
    else await load(true);
    setLoading(false);
    setCooldown(cooldownSeconds);
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
    <Shell onSalir={onSalir} counter>
      <main className="board">
        <div className="board-scroll">
          {expired ? (
            <p className="banner expired" role="status">
              <span>Sincronización pausada. Para seguir recibiendo avisos, extendé tu acceso.</span>
              <Link to="/pagar">Extender acceso</Link>
            </p>
          ) : trial ? (
            <p className="banner trial">La prueba está en curso.</p>
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
          <ol className="transfers">
            {items.map((item) => {
              const tone = item.status === "approved" || item.status === "pending" || item.status === "rejected" ? item.status : "other";
              const badge = item.status === "approved" ? "Acreditado" : item.status === "pending" ? "Pendiente" : item.statusLabel;
              const moment = new Date(item.occurredAt);
              const detail =
                item.status === "approved"
                  ? `${item.statusLabel} ${clock.format(moment)}`
                  : item.status === "pending"
                    ? "En proceso de acreditación"
                    : when.format(moment);
              return (
                <li key={item.id} className={first === item.id ? `row ${tone} focus` : `row ${tone}`}>
                  <div className="payer">{item.payerName}</div>
                  <div className="amount">{money.format(item.amount)}</div>
                  <div className="when">{detail}</div>
                  <div className="status">{badge}</div>
                </li>
              );
            })}
          </ol>
        </div>
        {wait ? <p className="wait" role="status">{wait}</p> : null}
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
      </main>
    </Shell>
  );
}

function Users({ session, onSalir }: { session: Session; onSalir: () => Promise<void> }) {
  return (
    <Shell onSalir={onSalir} back>
      <main>
        <h1>Configurar</h1>
        {session.isOwner ? <OwnerLogins /> : null}
      </main>
    </Shell>
  );
}

function OwnerLogins() {
  const [rows, setRows] = useState<LoginRow[]>([]);
  const [error, setError] = useState("");
  const [confirming, setConfirming] = useState<string | null>(null);

  async function reload() {
    const response = await api("/api/logins");
    if (response.ok) setRows(await response.json());
  }

  useEffect(() => {
    void reload();
  }, []);

  async function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const response = await api("/api/logins", {
      method: "POST",
      body: JSON.stringify({ email: data.get("email"), password: data.get("password") })
    });
    if (!response.ok) {
      setError(response.status === 409 ? "No se puede agregar otro Punto de Alerta en este plan." : "No se pudo crear la cuenta.");
      return;
    }
    event.currentTarget.reset();
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

  return (
    <section className="logins">
      {rows.map((row) => (
        <div className="login-row" key={row.id}>
          <span className="email">{row.email}</span>
          <span className="role">{row.isOwner ? "Admin" : "Punto de Alerta"}</span>
          {row.revoked ? null : (
            <label>
              <input type="checkbox" checked={row.pushApproved} onChange={(event) => void push(row, event.target.checked)} />
              Avisos
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
      <form onSubmit={add}>
        <label>
          Dirección de email *
          <input name="email" type="email" inputMode="email" required />
        </label>
        <label>
          Contraseña *
          <input name="password" type="password" minLength={8} required />
        </label>
        {error ? <p className="error" role="alert">{error}</p> : null}
        <button type="submit">Agregar cuenta</button>
      </form>
    </section>
  );
}

function Pay({ onSalir }: { onSalir: () => Promise<void> }) {
  const [price, setPrice] = useState<{ amount: number; currency: string; paidPeriodDays: number } | null>(null);

  useEffect(() => {
    api("/api/billing").then(async (response) => {
      if (response.ok) setPrice(await response.json());
    });
  }, []);

  async function pay() {
    const response = await api("/api/billing/checkout", { method: "POST" });
    if (!response.ok) return;
    const body = await response.json();
    window.location.assign(body.initPoint);
  }

  return (
    <Shell onSalir={onSalir} back>
      {!price ? <main /> : (
        <>
          <main className="pay">
            <h1>Extendé el acceso</h1>
            <p className="price">
              <span className="amount">{money.format(price.amount)}</span>
              <span className="period">cada {price.paidPeriodDays} días</span>
            </p>
          </main>
          <Dock>
            <button type="button" onClick={pay}>Pagar con Mercado Pago</button>
          </Dock>
        </>
      )}
    </Shell>
  );
}
