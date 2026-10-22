import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, Route, Routes, useNavigate, useSearchParams } from "react-router-dom";
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
const money = new Intl.NumberFormat("es-AR", { style: "currency", currency: "ARS" });
const when = new Intl.DateTimeFormat("es-AR", { dateStyle: "short", timeStyle: "short" });

function beep() {
  const context = new AudioContext();
  const tone = context.createOscillator();
  const gain = context.createGain();
  tone.frequency.value = 880;
  gain.gain.value = 0.08;
  tone.connect(gain);
  gain.connect(context.destination);
  tone.start();
  tone.stop(context.currentTime + 0.15);
  tone.onended = () => void context.close();
}

export function App() {
  return (
    <Routes>
      <Route path="/cuenta" element={<CreateAccount />} />
      <Route path="/pagar" element={<Pay />} />
      <Route path="*" element={<Home />} />
    </Routes>
  );
}

function CreateAccount() {
  const navigate = useNavigate();
  const [error, setError] = useState("");

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
    navigate("/");
  }

  return (
    <main>
      <h1>Creá tu cuenta</h1>
      <form onSubmit={submit}>
        <label>
          Correo
          <input name="email" type="email" autoComplete="username" required />
        </label>
        <label>
          Contraseña
          <input name="password" type="password" autoComplete="new-password" minLength={8} required />
        </label>
        {error ? <p className="error">{error}</p> : null}
        <button type="submit">Creá tu cuenta</button>
      </form>
    </main>
  );
}

function Enter() {
  const navigate = useNavigate();
  const [error, setError] = useState("");

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
    navigate("/");
  }

  return (
    <main>
      <h1>TransfersCheck</h1>
      <form onSubmit={submit}>
        <label>
          Correo
          <input name="email" type="email" autoComplete="username" required />
        </label>
        <label>
          Contraseña
          <input name="password" type="password" autoComplete="current-password" required />
        </label>
        {error ? <p className="error">{error}</p> : null}
        <button type="submit">Entrá</button>
      </form>
      <p>
        <Link to="/cuenta">Creá tu cuenta</Link>
      </p>
    </main>
  );
}

function Home() {
  const [session, setSession] = useState<Session | null | undefined>(undefined);
  const [checklist, setChecklist] = useState<Checklist | null>(null);

  useEffect(() => {
    let gone = false;
    api("/api/session").then(async (response) => {
      if (gone) return;
      if (!response.ok) {
        setSession(null);
        return;
      }
      setSession(await response.json());
      const list = await api("/api/checklist");
      if (!gone && list.ok) setChecklist(await list.json());
    });
    return () => {
      gone = true;
    };
  }, []);

  if (session === undefined || (session && !checklist)) return <main>TransfersCheck</main>;
  if (!session) return <Enter />;
  if (!checklist!.connectDone) return <ChecklistView checklist={checklist!} />;
  return <Transfers session={session} checklist={checklist!} />;
}

function ChecklistView({ checklist }: { checklist: Checklist }) {
  const [error, setError] = useState("");

  async function connect() {
    const response = await api("/api/connections/mercadopago", { method: "POST" });
    if (!response.ok) {
      setError("No se pudo empezar la conexión.");
      return;
    }
    const body = await response.json();
    window.location.assign(body.authorizationUrl);
  }

  return (
    <main>
      <h1>{checklist.accountDone ? checklist.connectTitle : checklist.accountTitle}</h1>
      <p className="help">{checklist.help}</p>
      {checklist.videoUrl ? <iframe title="Mercado Pago" src={checklist.videoUrl} /> : null}
      {error ? <p className="error">{error}</p> : null}
      <button type="button" onClick={connect}>{checklist.button}</button>
    </main>
  );
}

function Transfers({ session, checklist }: { session: Session; checklist: Checklist }) {
  const [params] = useSearchParams();
  const [items, setItems] = useState<Transfer[]>([]);
  const [notice, setNotice] = useState("");
  const [wait, setWait] = useState("");
  const [busy, setBusy] = useState(false);
  const [alertsDone, setAlertsDone] = useState(checklist.alertsDone);
  const [trial, setTrial] = useState(checklist.trialRunning);
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
  }

  function announceArrivals(next: Transfer[]) {
    const previous = seen.current;
    seen.current = new Map(next.map((item) => [item.id, item.status]));
    if (!previous) return;
    const arrived = next.find((item) => item.status === "approved" && previous.get(item.id) !== "approved");
    if (!arrived) return;
    beep();
    setNotice(`${arrived.statusLabel} · ${arrived.payerName} · ${money.format(arrived.amount)}`);
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

  async function refresh() {
    setBusy(true);
    setWait("");
    const response = await api("/api/transfers/refresh", { method: "POST" });
    if (response.status === 429) setWait("Esperá un momento.");
    else await load(true);
    window.setTimeout(() => setBusy(false), cooldownSeconds * 1000);
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

  return (
    <main>
      <h1>TransfersCheck</h1>
      {trial ? <p>La prueba está en curso.</p> : null}
      {notice ? <p className="notice" role="status">{notice}</p> : null}
      {!alertsDone ? (
        <section>
          <h2>Activá los avisos</h2>
          <p>En el iPhone, agregá la app a la pantalla de inicio.</p>
          <button type="button" onClick={enableAlerts}>Activá los avisos</button>
        </section>
      ) : null}
      <ol>
        {items.map((item) => (
          <li key={item.id}>
            <div>{item.payerName}</div>
            <div className="amount">{money.format(item.amount)}</div>
            <div>{when.format(new Date(item.occurredAt))}</div>
            <div>{item.statusLabel}</div>
          </li>
        ))}
      </ol>
      {wait ? <p>{wait}</p> : null}
      <p><Link to="/pagar">Pagar</Link></p>
      {session.isOwner ? <OwnerLogins /> : null}
      <button className="refresh" type="button" onClick={refresh} disabled={busy}>Actualizar</button>
    </main>
  );
}

function OwnerLogins() {
  const [rows, setRows] = useState<LoginRow[]>([]);
  const [error, setError] = useState("");

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
      setError("No se pudo crear la cuenta.");
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
    await api(`/api/logins/${row.id}`, { method: "DELETE" });
    await reload();
  }

  return (
    <section>
      {rows.map((row) => (
        <p key={row.id}>
          {row.email}
          {row.isOwner ? null : (
            <>
              <label>
                <input type="checkbox" checked={row.pushApproved} onChange={(event) => void push(row, event.target.checked)} />
                Avisos
              </label>
              <button type="button" onClick={() => void revoke(row)} disabled={row.revoked}>Revocar</button>
            </>
          )}
        </p>
      ))}
      <form onSubmit={add}>
        <label>
          Correo
          <input name="email" type="email" required />
        </label>
        <label>
          Contraseña
          <input name="password" type="password" minLength={8} required />
        </label>
        {error ? <p className="error">{error}</p> : null}
        <button type="submit">Agregar cuenta</button>
      </form>
    </section>
  );
}

function Pay() {
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

  if (!price) return <main>TransfersCheck</main>;
  return (
    <main>
      <h1>Pagar</h1>
      <p className="amount">{price.currency} {price.amount} cada {price.paidPeriodDays} días</p>
      <button type="button" onClick={pay}>Pagar</button>
    </main>
  );
}
