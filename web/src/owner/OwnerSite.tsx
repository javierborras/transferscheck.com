import { Fragment, useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Logo } from "../Logo";
import { api, withCorrelation } from "../api";
import { ActivityChart } from "./ActivityChart";
import { BillingOrders, type BillingOrder as BillingRow } from "./BillingOrders";

type Section = "inicio" | "clientes" | "dispositivos" | "facturaciones" | "movimientos" | "planes" | "servicios" | "helpdesk" | "errores" | "ficha";

type OwnerCard = { card: string; isSet: boolean; lastFour: string | null; expiresAt: string | null };
type OwnerPlan = {
  code: string;
  displayName: string;
  amount: number;
  currency: string;
  durationDays?: number | null;
  durationHours?: number | null;
  includedAlertPoints: number;
  faceCopy: string;
  enableCopilotHelp: boolean;
  active: boolean;
};
type TaskCard = { code: string; title: string; count: number };
type HealthCard = { code: string; title: string; value: number };
type ClientRow = {
  tenantId: string;
  alias: string | null;
  email: string;
  planCode: string | null;
  accessUntil: string | null;
  trialEndsAt: string | null;
  connected: boolean;
  paidOrdersCount?: number;
  totalPaidAmount?: number;
  lastPaymentAt?: string | null;
  mercadoPago?: string;
  accessState?: string;
  tone?: string;
  toneLabel?: string;
  pendingPayment?: boolean;
  loginCount?: number;
  pushDeviceCount?: number;
};
type DeviceRow = {
  id: string;
  tenantId: string;
  alias: string | null;
  email: string;
  accountLabel: string;
  alertPoint: boolean;
};
type IncidentRow = {
  fingerprint: string;
  count: number;
  releaseTag: string | null;
  sentryId: string | null;
  sentryUrl: string | null;
  githubIssueNumber: number | null;
  issueUrl: string | null;
  pullRequestUrl: string | null;
};
type ErrorLogRow = {
  occurredAt: string;
  correlationId: string;
  message: string;
  path: string;
  level: string;
};
type HelpMessageRow = {
  id: string;
  alias: string | null;
  email: string;
  text: string;
  createdAt: string;
  closedAt: string | null;
};
type OwnerMovementRow = {
  id: string;
  externalId: string;
  occurredAt: string;
  approvedAt?: string | null;
  payerName: string;
  amount: number;
  currency: string;
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

const sections: { id: Section; label: string }[] = [
  { id: "inicio", label: "Inicio" },
  { id: "clientes", label: "Clientes" },
  { id: "dispositivos", label: "Dispositivos" },
  { id: "facturaciones", label: "Facturaciones" },
  { id: "movimientos", label: "Actividad MP" },
  { id: "planes", label: "Planes" },
  { id: "servicios", label: "Servicios" },
  { id: "helpdesk", label: "Helpdesk" },
  { id: "errores", label: "Errores" }
];

const ownerCardLabels: Record<string, string> = {
  mercadopago_platform: "Mercado Pago (plataforma)",
  mail: "Mail",
  browser_push: "Notificaciones web",
  whatsapp: "WhatsApp"
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

function stamp(value: string | null) {
  return value ? when.format(new Date(value)) : "—";
}

function planFace(code: string | null | undefined) {
  if (code === "pack_mes") return "Pack Mes";
  if (code === "pack_evento") return "Pack Evento";
  if (code === "pack_corporativo") return "Pack Corporativo";
  return code || "—";
}

function parseSection(value: string | null): Section {
  if (value === "ficha") return "ficha";
  return sections.some((item) => item.id === value) ? (value as Section) : "inicio";
}

function healthTarget(code: string): { section: Section; extra?: Record<string, string | undefined> } {
  if (code === "connected") return { section: "clientes", extra: { mp: "vinculado" } };
  if (code === "rate_limited") return { section: "clientes", extra: { mp: "espera" } };
  if (code === "push_devices") return { section: "dispositivos" };
  if (code === "checks_stale") return { section: "clientes", extra: { mp: "demoradas" } };
  return { section: "clientes" };
}

function healthAction(code: string) {
  if (code === "connected") return "Ver vinculados";
  if (code === "rate_limited") return "Ver en espera";
  if (code === "push_devices") return "Ver dispositivos";
  if (code === "checks_stale") return "Ver demoradas";
  return "Ver comercios";
}

function mpWords(value: string | undefined, connected: boolean) {
  if (value === "reconectar") return "Hay que volver a vincular";
  if (value === "vinculado" || connected) return "Vinculado";
  return "Sin vincular";
}

function toneClass(tone: string | undefined) {
  if (tone === "verde") return "approved";
  if (tone === "rojo") return "rejected";
  return "pending";
}

function filterLead(mp: string, access: string, trial: string, plan: string, q: string) {
  if (mp === "vinculado") return "Comercios vinculados a Mercado Pago.";
  if (mp === "espera") return "Comercios en espera del proveedor.";
  if (mp === "demoradas") return "Comercios vinculados cuya consulta está demorada.";
  if (mp === "reconectar") return "Comercios que tienen que volver a vincular Mercado Pago.";
  if (mp === "sin_vincular") return "Comercios sin Mercado Pago vinculado.";
  if (access === "vigente") return "Comercios con acceso vigente.";
  if (access === "vence") return "Comercios cuyo acceso vence pronto.";
  if (access === "vencido") return "Comercios con el acceso vencido.";
  if (access === "sin") return "Comercios sin acceso.";
  if (trial === "vigente") return "Comercios con prueba vigente.";
  if (trial === "no") return "Comercios sin prueba vigente.";
  if (plan || q) return "Comercios que coinciden con los filtros.";
  return "";
}

function EmptyState({ colSpan, text }: { colSpan: number; text: string }) {
  return (
    <tr className="owner-empty-row">
      <td colSpan={colSpan}>
        <div className="owner-empty">
          <span className="owner-empty-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
              <rect x="3.5" y="4.5" width="17" height="15" rx="2" />
              <path d="M7 9h10M7 13h10M7 17h6" />
            </svg>
          </span>
          <span>{text}</span>
        </div>
      </td>
    </tr>
  );
}

export function OwnerSite() {
  const [params, setParams] = useSearchParams();
  const section = parseSection(params.get("seccion"));
  const [email, setEmail] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [tasks, setTasks] = useState<TaskCard[]>([]);
  const [health, setHealth] = useState<HealthCard[]>([]);
  const [clients, setClients] = useState<ClientRow[]>([]);
  const [clientsLoaded, setClientsLoaded] = useState(false);
  const [clientsError, setClientsError] = useState("");
  const [billing, setBilling] = useState<BillingRow[]>([]);
  const [billingLoaded, setBillingLoaded] = useState(false);
  const [tenantFilter, setTenantFilter] = useState("");
  const [expandedClientId, setExpandedClientId] = useState<string | null>(null);
  const [clientOrders, setClientOrders] = useState<Record<string, BillingRow[]>>({});
  const [loadingClientOrders, setLoadingClientOrders] = useState<string | null>(null);
  const [movements, setMovements] = useState<OwnerMovementRow[]>([]);
  const [movementsLoaded, setMovementsLoaded] = useState(false);
  const [movementsLoading, setMovementsLoading] = useState(false);
  const [movementsError, setMovementsError] = useState("");
  const [movFrom, setMovFrom] = useState("");
  const [movTo, setMovTo] = useState("");
  const [movStatus, setMovStatus] = useState("");
  const [movType, setMovType] = useState("");
  const [movOffset, setMovOffset] = useState(0);
  const [movNext, setMovNext] = useState<number | null>(null);
  const [selectedMovement, setSelectedMovement] = useState<OwnerMovementRow | null>(null);
  const movementDialog = useRef<HTMLDialogElement>(null);
  const [incidents, setIncidents] = useState<IncidentRow[]>([]);
  const [helpMessages, setHelpMessages] = useState<HelpMessageRow[]>([]);
  const [helpdeskLoaded, setHelpdeskLoaded] = useState(false);
  const [helpdeskError, setHelpdeskError] = useState("");
  const [closingMessageId, setClosingMessageId] = useState<string | null>(null);
  const [errorLogs, setErrorLogs] = useState<ErrorLogRow[]>([]);
  const [errorLogsLoaded, setErrorLogsLoaded] = useState(false);
  const [errorLogsError, setErrorLogsError] = useState("");
  const [cards, setCards] = useState<OwnerCard[]>([]);
  const [plans, setPlans] = useState<OwnerPlan[]>([]);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [planDrafts, setPlanDrafts] = useState<Record<string, OwnerPlan>>({});
  const [devices, setDevices] = useState<DeviceRow[]>([]);
  const [devicesLoaded, setDevicesLoaded] = useState(false);
  const [devicesError, setDevicesError] = useState("");
  const [ficha, setFicha] = useState<ClientRow | null>(null);
  const [fichaError, setFichaError] = useState("");
  const [fichaOrders, setFichaOrders] = useState<BillingRow[]>([]);
  const [fichaOrdersLoaded, setFichaOrdersLoaded] = useState(false);
  const [editing, setEditing] = useState<ClientRow | null>(null);
  const [editAlias, setEditAlias] = useState("");
  const [editPlan, setEditPlan] = useState("pack_mes");
  const [editError, setEditError] = useState("");
  const [editSaving, setEditSaving] = useState(false);
  const editDialog = useRef<HTMLDialogElement>(null);
  const q = params.get("q") ?? "";
  const planFilter = params.get("plan") ?? "";
  const mpFilter = params.get("mp") ?? "";
  const accessFilter = params.get("access") ?? "";
  const trialFilter = params.get("trial") ?? "";
  const filtersOn = Boolean(q || planFilter || mpFilter || accessFilter || trialFilter);
  const [qDraft, setQDraft] = useState(q);

  async function loadHome() {
    const response = await api("/api/owner/home");
    if (!response.ok) return;
    const body = (await response.json()) as { tasks: TaskCard[]; health: HealthCard[] };
    setTasks(body.tasks);
    setHealth(body.health);
  }

  function go(next: Section, extra?: Record<string, string | undefined>) {
    const nextParams = new URLSearchParams();
    if (next !== "inicio") nextParams.set("seccion", next);
    if (extra) {
      for (const [key, value] of Object.entries(extra)) {
        if (value) nextParams.set(key, value);
      }
    }
    setParams(nextParams);
  }

  function setClientFilter(patch: Record<string, string>) {
    const next = new URLSearchParams(params);
    next.set("seccion", "clientes");
    const draft = patch.q !== undefined ? patch.q : qDraft;
    if (draft.trim()) next.set("q", draft.trim());
    else next.delete("q");
    for (const [key, value] of Object.entries(patch)) {
      if (key === "q") continue;
      if (value) next.set(key, value);
      else next.delete(key);
    }
    setParams(next);
  }

  function beginEdit(client: ClientRow) {
    setEditAlias(client.alias ?? "");
    setEditPlan(client.planCode || "pack_mes");
    setEditError("");
    setEditing(client);
  }

  async function loadClients(search: URLSearchParams) {
    try {
      const query = new URLSearchParams();
      for (const key of ["q", "plan", "mp", "access", "trial"]) {
        const value = search.get(key);
        if (value) query.set(key, value);
      }
      const suffix = query.toString();
      const response = await api(suffix ? `/api/owner/clients?${suffix}` : "/api/owner/clients");
      if (!response.ok) {
        setClients([]);
        setClientsLoaded(true);
        setClientsError(withCorrelation(response, "No se pudieron cargar los comercios."));
        return;
      }
      const body = await response.json();
      if (!Array.isArray(body)) {
        setClients([]);
        setClientsLoaded(true);
        setClientsError("No se pudieron cargar los comercios.");
        return;
      }
      setClients(body);
      setClientsLoaded(true);
      setClientsError("");
    } catch (caught) {
      setClients([]);
      setClientsLoaded(true);
      setClientsError(withCorrelation(caught, "No se pudieron cargar los comercios."));
    }
  }

  async function loadHelpdesk() {
    setHelpdeskError("");
    try {
      const [incidentResponse, messageResponse] = await Promise.all([
        api("/api/owner/helpdesk/incidents"),
        api("/api/owner/helpdesk/messages")
      ]);
      if (!incidentResponse.ok || !messageResponse.ok) {
        const failed = !incidentResponse.ok ? incidentResponse : messageResponse;
        setHelpdeskError(withCorrelation(failed, "No se pudo cargar el Helpdesk."));
        setHelpdeskLoaded(true);
        return;
      }
      setIncidents(await incidentResponse.json());
      setHelpMessages(await messageResponse.json());
      setHelpdeskLoaded(true);
    } catch (caught) {
      setHelpdeskError(withCorrelation(caught, "No se pudo cargar el Helpdesk."));
      setHelpdeskLoaded(true);
    }
  }

  async function loadErrorLogs() {
    setErrorLogsError("");
    try {
      const response = await api("/api/owner/logs");
      if (!response.ok) {
        setErrorLogsError(withCorrelation(response, "No se pudieron cargar los errores."));
        setErrorLogsLoaded(true);
        return;
      }
      const body = await response.json();
      setErrorLogs(Array.isArray(body) ? body : []);
      setErrorLogsLoaded(true);
    } catch (caught) {
      setErrorLogsError(withCorrelation(caught, "No se pudieron cargar los errores."));
      setErrorLogsLoaded(true);
    }
  }

  async function closeHelpMessage(id: string) {
    setClosingMessageId(id);
    setHelpdeskError("");
    try {
      const response = await api(`/api/owner/helpdesk/messages/${id}/close`, { method: "POST" });
      if (!response.ok) {
        setHelpdeskError(withCorrelation(response, "No se pudo cerrar el mensaje."));
        return;
      }
      const closed = (await response.json()) as HelpMessageRow;
      setHelpMessages((current) => current.map((item) => (item.id === closed.id ? closed : item)));
    } catch (caught) {
      setHelpdeskError(withCorrelation(caught, "No se pudo cerrar el mensaje."));
    } finally {
      setClosingMessageId(null);
    }
  }

  async function loadDevices() {
    setDevicesError("");
    try {
      const response = await api("/api/owner/devices");
      if (!response.ok) {
        setDevicesError(withCorrelation(response, "No se pudieron cargar los dispositivos."));
        setDevicesLoaded(true);
        return;
      }
      const body = await response.json();
      setDevices(Array.isArray(body) ? body : []);
      setDevicesLoaded(true);
    } catch (caught) {
      setDevicesError(withCorrelation(caught, "No se pudieron cargar los dispositivos."));
      setDevicesLoaded(true);
    }
  }

  async function loadFicha(tenantId: string) {
    if (!tenantId) {
      setFicha(null);
      setFichaError("Elegí un comercio desde Clientes.");
      setFichaOrders([]);
      setFichaOrdersLoaded(true);
      return;
    }
    setFichaError("");
    setFichaOrdersLoaded(false);
    try {
      const [clientResponse, billingResponse] = await Promise.all([
        api(`/api/owner/clients/${tenantId}`),
        api(`/api/owner/billing?tenantId=${encodeURIComponent(tenantId)}`)
      ]);
      if (!clientResponse.ok) {
        setFicha(null);
        setFichaError(clientResponse.status === 404 ? "Ese comercio no está en la lista." : withCorrelation(clientResponse, "No se pudo abrir la ficha."));
        setFichaOrdersLoaded(true);
        return;
      }
      setFicha((await clientResponse.json()) as ClientRow);
      setFichaOrders(billingResponse.ok ? ((await billingResponse.json()) as BillingRow[]) : []);
      setFichaOrdersLoaded(true);
    } catch (caught) {
      setFicha(null);
      setFichaError(withCorrelation(caught, "No se pudo abrir la ficha."));
      setFichaOrdersLoaded(true);
    }
  }

  async function saveClient(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editing) return;
    setEditSaving(true);
    setEditError("");
    try {
      const response = await api(`/api/owner/clients/${editing.tenantId}`, {
        method: "PUT",
        body: JSON.stringify({ alias: editAlias, planCode: editPlan })
      });
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        setEditError(
          body.error === "alias_taken"
            ? "Ese alias ya está en otro comercio."
            : body.error === "invalid_alias"
              ? "El alias no sirve. Usá letras, números y un guión, sin el #."
              : body.error === "invalid_plan"
                ? "Ese plan no existe."
                : withCorrelation(response, "No se pudo guardar el comercio.")
        );
        return;
      }
      const saved = (await response.json()) as ClientRow;
      setClients((current) => current.map((item) => (item.tenantId === saved.tenantId ? saved : item)));
      if (ficha?.tenantId === saved.tenantId) setFicha(saved);
      setEditing(null);
    } catch (caught) {
      setEditError(withCorrelation(caught, "No se pudo guardar el comercio."));
    } finally {
      setEditSaving(false);
    }
  }

  async function loadBilling(tenantId: string) {
    const query = tenantId ? `?tenantId=${encodeURIComponent(tenantId)}` : "";
    const response = await api(`/api/owner/billing${query}`);
    if (!response.ok) return;
    setBilling(await response.json());
    setBillingLoaded(true);
  }

  async function toggleClientHistory(tenantId: string) {
    if (expandedClientId === tenantId) {
      setExpandedClientId(null);
      return;
    }
    setExpandedClientId(tenantId);
    if (clientOrders[tenantId]) return;
    setLoadingClientOrders(tenantId);
    try {
      const response = await api(`/api/owner/billing?tenantId=${encodeURIComponent(tenantId)}`);
      if (response.ok) {
        const rows = (await response.json()) as BillingRow[];
        setClientOrders((current) => ({ ...current, [tenantId]: rows }));
      }
    } finally {
      setLoadingClientOrders(null);
    }
  }

  async function loadMovements(start = 0) {
    setMovementsLoading(true);
    setMovementsError("");
    try {
      const query = new URLSearchParams({ offset: String(start) });
      if (movFrom) query.set("from", new Date(`${movFrom}T00:00:00`).toISOString());
      if (movTo) query.set("to", new Date(`${movTo}T23:59:59.999`).toISOString());
      if (movStatus) query.set("status", movStatus);
      if (movType) query.set("operationType", movType);
      const response = await api(`/api/owner/movements?${query}`);
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        setMovementsError(
          body.error === "token_not_configured"
            ? "Configurá el token de Mercado Pago (plataforma) en Servicios para consultar todos tus movimientos."
            : withCorrelation(response, "No se pudieron cargar los movimientos de Mercado Pago en este momento.")
        );
        return;
      }
      const body = await response.json();
      setMovements(body.items ?? []);
      setMovOffset(body.offset ?? start);
      setMovNext(body.nextOffset ?? null);
      setMovementsLoaded(true);
    } catch (caught) {
      setMovementsError(withCorrelation(caught, "Error de conexión al consultar los movimientos de Mercado Pago."));
    } finally {
      setMovementsLoading(false);
    }
  }

  useEffect(() => {
    setQDraft(q);
  }, [q]);

  useEffect(() => {
    const dialog = editDialog.current;
    if (!dialog) return;
    if (editing) {
      if (!dialog.open) dialog.showModal();
      return;
    }
    if (dialog.open) dialog.close();
  }, [editing]);

  useEffect(() => {
    const dialog = movementDialog.current;
    if (!dialog) return;
    if (selectedMovement) {
      if (!dialog.open) dialog.showModal();
      return;
    }
    if (dialog.open) dialog.close();
  }, [selectedMovement]);

  async function loadPlans() {
    const response = await api("/api/owner/plans");
    if (!response.ok) return;
    const nextPlans = (await response.json()) as OwnerPlan[];
    setPlans(nextPlans);
    setPlanDrafts(Object.fromEntries(nextPlans.map((plan) => [plan.code, { ...plan }])));
  }

  async function loadServices() {
    const response = await api("/api/owner/services");
    if (!response.ok) return;
    setCards(await response.json());
  }

  useEffect(() => {
    let gone = false;
    const initial = new URLSearchParams(window.location.search);
    const ticket = initial.get("ticket");
    void (async () => {
      if (ticket) {
        const exchanged = await api("/api/owner/sessions/ticket", {
          method: "POST",
          body: JSON.stringify({ ticket })
        });
        initial.delete("ticket");
        setParams(initial, { replace: true });
        if (gone) return;
        if (!exchanged.ok) {
          setEmail(null);
          setError("El enlace de acceso ya no sirve.");
          return;
        }
      }
      const response = await api("/api/owner/session");
      if (gone) return;
      if (!response.ok) {
        setEmail(null);
        return;
      }
      const body = (await response.json()) as { email: string };
      setEmail(body.email);
      await loadHome();
    })();
    return () => {
      gone = true;
    };
  }, []);

  useEffect(() => {
    if (!email) return;
    if (section === "inicio") void loadHome();
    if (section === "clientes" || section === "facturaciones") void loadClients(params);
    if (section === "facturaciones") void loadBilling(tenantFilter);
    if (section === "movimientos") void loadMovements(0);
    if (section === "planes") void loadPlans();
    if (section === "servicios") void loadServices();
    if (section === "helpdesk") void loadHelpdesk();
    if (section === "errores") void loadErrorLogs();
    if (section === "dispositivos") void loadDevices();
    if (section === "ficha") void loadFicha(params.get("cliente") ?? "");
  }, [email, section, params, tenantFilter]);

  async function signIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const response = await api("/api/owner/sessions", {
      method: "POST",
      body: JSON.stringify({ email: data.get("email"), password: data.get("password") })
    });
    if (!response.ok) {
      setError(response.status >= 500 ? withCorrelation(response, "No pudimos abrir la sesión.") : "El correo o la contraseña no sirven.");
      return;
    }
    const body = (await response.json()) as { email: string };
    setEmail(body.email);
    setError("");
    go("inicio");
  }

  async function signOut() {
    await api("/api/owner/sessions", { method: "DELETE" });
    setEmail(null);
    setTasks([]);
    setHealth([]);
    setClients([]);
    setClientsLoaded(false);
    setClientsError("");
    setBilling([]);
    setCards([]);
    setPlans([]);
    setIncidents([]);
    setHelpMessages([]);
    setHelpdeskLoaded(false);
    setHelpdeskError("");
    setErrorLogs([]);
    setErrorLogsLoaded(false);
    setErrorLogsError("");
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
      setError(withCorrelation(response, "No se pudo guardar el secreto."));
      return;
    }
    setDrafts((value) => ({ ...value, [card]: "" }));
    setError("");
    await loadServices();
  }

  async function savePlan(code: string) {
    const draft = planDrafts[code];
    if (!draft) return;
    const response = await api(`/api/owner/plans/${encodeURIComponent(code)}`, {
      method: "PUT",
      body: JSON.stringify(draft)
    });
    if (!response.ok) {
      setError(withCorrelation(response, "No se pudo guardar el plan."));
      return;
    }
    setError("");
    await loadPlans();
  }

  if (!email) {
    return (
      <main className="gate">
        <Logo />
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
    <div className="owner-shell">
      <nav className="owner-nav" aria-label="Secciones del owner">
        <Logo compact />
        {sections.map((item) => (
          <button
            key={item.id}
            type="button"
            aria-current={section === item.id || (section === "ficha" && item.id === "clientes") ? "page" : undefined}
            onClick={() => go(item.id)}
          >
            {item.label}
          </button>
        ))}
      </nav>
      <div className="owner-main">
        <header className="app-header owner-topbar">
          <div className="owner-topbar-id">
            <span className="owner-who">{email}</span>
            <span className="owner-console-badge">Consola Owner</span>
          </div>
          <button className="secondary" type="button" onClick={() => void signOut()}>
            Salir
          </button>
        </header>
        <div className="owner-content">
        {error ? <p className="error">{error}</p> : null}
        {section === "inicio" ? (
          <>
            <h1>Inicio</h1>
            <p className="gate-lead">Tareas del producto y el estado de las consultas.</p>
            <h2>Tareas pendientes</h2>
            {tasks.length === 0 ? (
              <div className="owner-clear" role="status">
                ✓ Todo al día. No hay tareas pendientes de revisión.
              </div>
            ) : (
              <ul className="owner-cards">
                {tasks.map((task) => (
                  <li key={task.code} className="owner-card">
                    <span>{task.title}</span>
                    <strong>{task.count}</strong>
                  </li>
                ))}
              </ul>
            )}
            <h2>Salud</h2>
            <ul className="owner-cards owner-health">
              {health.map((item) => {
                const alert = (item.code === "rate_limited" || item.code === "checks_stale") && item.value > 0;
                const target = healthTarget(item.code);
                return (
                  <li key={item.code}>
                    <button
                      type="button"
                      className={alert ? "owner-card alert" : "owner-card"}
                      onClick={() => go(target.section, target.extra)}
                    >
                      <span>{item.title}</span>
                      <strong>{item.value}</strong>
                      <span className="owner-card-action">{healthAction(item.code)}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
            <h2>Actividad</h2>
            <p className="gate-lead">Altas de comercios y consultas a la API de Mercado Pago.</p>
            <ActivityChart />
          </>
        ) : null}
        {section === "helpdesk" ? (
          <>
            <h1>Helpdesk</h1>
            <p className="gate-lead">Incidentes repetidos y mensajes de los comercios. Un mensaje no publica código.</p>
            {helpdeskError ? <p className="error" role="alert">{helpdeskError}</p> : null}
            <h2>Incidentes</h2>
            <div className="owner-table-wrap" tabIndex={0} role="region" aria-label="Incidentes">
              <table className="owner-table">
                <thead>
                  <tr>
                    <th>Conteo</th>
                    <th>Tag</th>
                    <th>Issue</th>
                    <th>PR</th>
                    <th>Sentry</th>
                  </tr>
                </thead>
                <tbody>
                  {helpdeskLoaded && incidents.length === 0 ? (
                    <EmptyState colSpan={5} text="No hay incidentes." />
                  ) : null}
                  {incidents.map((incident) => (
                    <tr key={incident.fingerprint}>
                      <td>{incident.count}</td>
                      <td>{incident.releaseTag ?? "—"}</td>
                      <td>
                        {incident.issueUrl ? (
                          <a href={incident.issueUrl} target="_blank" rel="noopener noreferrer">
                            #{incident.githubIssueNumber}
                          </a>
                        ) : "—"}
                      </td>
                      <td>
                        {incident.pullRequestUrl ? (
                          <a href={incident.pullRequestUrl} target="_blank" rel="noopener noreferrer">PR</a>
                        ) : "—"}
                      </td>
                      <td>
                        {incident.sentryUrl ? (
                          <a href={incident.sentryUrl} target="_blank" rel="noopener noreferrer">Sentry</a>
                        ) : incident.sentryId ?? "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <h2>Mensajes</h2>
            <div className="owner-table-wrap" tabIndex={0} role="region" aria-label="Mensajes">
              <table className="owner-table">
                <thead>
                  <tr>
                    <th>Alias</th>
                    <th>Mail</th>
                    <th>Texto</th>
                    <th>Fecha</th>
                    <th>Estado</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {helpdeskLoaded && helpMessages.length === 0 ? (
                    <EmptyState colSpan={6} text="No hay mensajes." />
                  ) : null}
                  {helpMessages.map((message) => (
                    <tr key={message.id}>
                      <td>{message.alias ? `#${message.alias}` : "—"}</td>
                      <td className="owner-email">{message.email}</td>
                      <td>{message.text}</td>
                      <td className="stamp">{stamp(message.createdAt)}</td>
                      <td>{message.closedAt ? "Cerrado" : "Abierto"}</td>
                      <td>
                        {message.closedAt ? null : (
                          <button
                            type="button"
                            className="secondary small"
                            disabled={closingMessageId === message.id}
                            onClick={() => void closeHelpMessage(message.id)}
                          >
                            Cerrar
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        ) : null}
        {section === "errores" ? (
          <>
            <h1>Errores</h1>
            <p className="gate-lead">Fallos recientes del API. La referencia es el id de correlación. No hay trazas ni cuerpos de pago.</p>
            {errorLogsError ? <p className="error" role="alert">{errorLogsError}</p> : null}
            <div className="owner-table-wrap" tabIndex={0} role="region" aria-label="Errores recientes">
              <table className="owner-table">
                <thead>
                  <tr>
                    <th>Fecha</th>
                    <th>Referencia</th>
                    <th>Mensaje</th>
                    <th>Ruta</th>
                    <th>Nivel</th>
                  </tr>
                </thead>
                <tbody>
                  {errorLogsLoaded && errorLogs.length === 0 ? (
                    <EmptyState colSpan={5} text="No hay errores recientes." />
                  ) : null}
                  {errorLogs.map((row, index) => (
                    <tr key={`${row.occurredAt}-${row.correlationId}-${index}`}>
                      <td className="stamp">{stamp(row.occurredAt)}</td>
                      <td className="owner-log-ref">{row.correlationId || "—"}</td>
                      <td className="owner-log-message">{row.message}</td>
                      <td>{row.path || "—"}</td>
                      <td>{row.level}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        ) : null}
        {section === "clientes" ? (
          <>
            <h1>Clientes</h1>
            <p className="gate-lead">Cada comercio, su estado de suscripción y el historial de pagos hacia tu cuenta.</p>
            {filterLead(mpFilter, accessFilter, trialFilter, planFilter, q) ? (
              <p className="gate-lead">{filterLead(mpFilter, accessFilter, trialFilter, planFilter, q)}</p>
            ) : null}
            <div className="owner-client-summary" aria-label="Resumen de clientes">
              <div><span>Total</span><strong>{clients.length}</strong></div>
              <div><span>Vinculados</span><strong>{clients.filter((client) => client.connected).length}</strong></div>
              <div><span>Con prueba</span><strong>{clients.filter((client) => client.trialEndsAt).length}</strong></div>
              <div><span>Con pagos</span><strong>{clients.filter((client) => (client.paidOrdersCount ?? 0) > 0).length}</strong></div>
            </div>
            <form
              className="owner-filters"
              aria-label="Filtros de clientes"
              onSubmit={(event) => {
                event.preventDefault();
                setClientFilter({ q: qDraft });
              }}
            >
              <label>
                Alias o mail
                <input value={qDraft} onChange={(event) => setQDraft(event.target.value)} placeholder="Buscar" />
              </label>
              <label>
                Plan
                <select value={planFilter} onChange={(event) => setClientFilter({ plan: event.target.value })}>
                  <option value="">Todos</option>
                  <option value="pack_mes">Pack Mes</option>
                  <option value="pack_evento">Pack Evento</option>
                  <option value="pack_corporativo">Pack Corporativo</option>
                </select>
              </label>
              <label>
                Mercado Pago
                <select value={mpFilter} onChange={(event) => setClientFilter({ mp: event.target.value })}>
                  <option value="">Todos</option>
                  <option value="vinculado">Vinculados</option>
                  <option value="sin_vincular">Sin vincular</option>
                  <option value="reconectar">Hay que volver a vincular</option>
                  <option value="espera">En espera del proveedor</option>
                  <option value="demoradas">Consultas demoradas</option>
                </select>
              </label>
              <label>
                Acceso
                <select value={accessFilter} onChange={(event) => setClientFilter({ access: event.target.value })}>
                  <option value="">Todos</option>
                  <option value="vigente">Vigente</option>
                  <option value="vence">Vence pronto</option>
                  <option value="vencido">Vencido</option>
                  <option value="sin">Sin acceso</option>
                </select>
              </label>
              <label>
                Prueba
                <select value={trialFilter} onChange={(event) => setClientFilter({ trial: event.target.value })}>
                  <option value="">Todas</option>
                  <option value="vigente">Vigente</option>
                  <option value="no">Sin prueba vigente</option>
                </select>
              </label>
              <button type="submit">Buscar</button>
              {filtersOn ? (
                <button type="button" className="secondary" onClick={() => go("clientes")}>
                  Limpiar filtros
                </button>
              ) : null}
            </form>
            {clientsError ? <p className="error" role="alert">{clientsError}</p> : null}
            <div className="owner-table-wrap" tabIndex={0} role="region" aria-label="Comercios">
            <table className="owner-table owner-client-table">
              <thead>
                <tr>
                  <th>Alias</th>
                  <th>Mail</th>
                  <th>Plan</th>
                  <th>Estado</th>
                  <th>Acceso hasta</th>
                  <th>Prueba</th>
                  <th>Mercado Pago</th>
                  <th>Pagos acreditados</th>
                  <th>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {clientsLoaded && !clientsError && clients.length === 0 ? (
                  <EmptyState colSpan={9} text={filtersOn ? "Ningún comercio coincide con estos filtros." : "Todavía no hay comercios registrados."} />
                ) : null}
                {clients.map((client) => {
                  const isExpanded = expandedClientId === client.tenantId;
                  const orders = clientOrders[client.tenantId] ?? [];
                  const who = client.email || (client.alias ? `#${client.alias}` : "este comercio");
                  return (
                    <Fragment key={client.tenantId}>
                      <tr className={[isExpanded ? "expanded-row" : "", client.tone ? `tone-${client.tone}` : ""].filter(Boolean).join(" ") || undefined}>
                        <td>{client.alias ? `#${client.alias}` : "—"}</td>
                        <td className="owner-email">{client.email}</td>
                        <td>{client.planCode ? <span className="plan-badge">{planFace(client.planCode)}</span> : "—"}</td>
                        <td>
                          <span className={`status-pill ${toneClass(client.tone)}`}>{client.toneLabel || mpWords(client.mercadoPago, client.connected)}</span>
                        </td>
                        <td className="stamp">{stamp(client.accessUntil)}</td>
                        <td className="stamp">{stamp(client.trialEndsAt)}</td>
                        <td>{mpWords(client.mercadoPago, client.connected)}</td>
                        <td className="money">
                          {client.paidOrdersCount ? `${client.paidOrdersCount} (${money.format(client.totalPaidAmount ?? 0)})` : "0"}
                        </td>
                        <td>
                          <div className="owner-row-actions">
                            <Link className="button secondary small" to={`/owner?seccion=ficha&cliente=${client.tenantId}`} target="_blank" rel="noopener noreferrer">
                              Ver ficha
                            </Link>
                            <button type="button" className="secondary small" onClick={() => beginEdit(client)}>
                              Editar
                            </button>
                            <button
                              type="button"
                              className="secondary small"
                              onClick={() => void toggleClientHistory(client.tenantId)}
                            >
                              {isExpanded ? "Ocultar pagos" : "Ver pagos"}
                            </button>
                            <button
                              type="button"
                              className="secondary small"
                              onClick={() => {
                                setTenantFilter(client.tenantId);
                                go("facturaciones");
                              }}
                            >
                              Ir a Facturaciones
                            </button>
                          </div>
                        </td>
                      </tr>
                      {isExpanded ? (
                        <tr className="client-history-drawer">
                          <td colSpan={9}>
                            <BillingOrders
                              orders={orders}
                              loading={loadingClientOrders === client.tenantId}
                              caption={`Facturas de ${who}`}
                            />
                          </td>
                        </tr>
                      ) : null}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
            </div>
          </>
        ) : null}
        {section === "dispositivos" ? (
          <>
            <h1>Dispositivos con aviso</h1>
            <p className="gate-lead">Cada dispositivo registrado para recibir un aviso de transferencia.</p>
            {devicesError ? <p className="error" role="alert">{devicesError}</p> : null}
            <div className="owner-table-wrap" tabIndex={0} role="region" aria-label="Dispositivos con aviso">
              <table className="owner-table">
                <thead>
                  <tr>
                    <th>Alias</th>
                    <th>Mail</th>
                    <th>Cuenta</th>
                    <th>Tipo</th>
                    <th>Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {devicesLoaded && !devicesError && devices.length === 0 ? (
                    <EmptyState colSpan={5} text="No hay dispositivos con aviso." />
                  ) : null}
                  {devices.map((device) => (
                    <tr key={device.id}>
                      <td>{device.alias ? `#${device.alias}` : "—"}</td>
                      <td className="owner-email">{device.email || "—"}</td>
                      <td>{device.accountLabel}</td>
                      <td>{device.alertPoint ? "Punto de Alerta" : "Administrador"}</td>
                      <td>
                        <Link className="button secondary small" to={`/owner?seccion=ficha&cliente=${device.tenantId}`} target="_blank" rel="noopener noreferrer">
                          Ver ficha
                        </Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        ) : null}
        {section === "ficha" ? (
          <>
            <p>
              <button type="button" className="secondary" onClick={() => go("clientes")}>Volver a clientes</button>
            </p>
            {fichaError ? <p className="error" role="alert">{fichaError}</p> : null}
            {ficha ? (
              <>
                <h1>{ficha.alias ? `#${ficha.alias}` : ficha.email || "Comercio"}</h1>
                <p className="gate-lead">{ficha.email}</p>
                <dl className="ficha-facts">
                  <div>
                    <dt>Plan</dt>
                    <dd>{ficha.planCode ? planFace(ficha.planCode) : "—"}</dd>
                  </div>
                  <div>
                    <dt>Acceso hasta</dt>
                    <dd>{stamp(ficha.accessUntil)}</dd>
                  </div>
                  <div>
                    <dt>Prueba</dt>
                    <dd>{stamp(ficha.trialEndsAt)}</dd>
                  </div>
                  <div>
                    <dt>Pagos acreditados</dt>
                    <dd className="money">{ficha.paidOrdersCount ? `${ficha.paidOrdersCount} (${money.format(ficha.totalPaidAmount ?? 0)})` : "0"}</dd>
                  </div>
                  <div>
                    <dt>Estado</dt>
                    <dd><span className={`status-pill ${toneClass(ficha.tone)}`}>{ficha.toneLabel || mpWords(ficha.mercadoPago, ficha.connected)}</span></dd>
                  </div>
                  <div>
                    <dt>Mercado Pago</dt>
                    <dd>{mpWords(ficha.mercadoPago, ficha.connected)}</dd>
                  </div>
                </dl>
                <p>
                  <button type="button" className="secondary" onClick={() => beginEdit(ficha)}>Editar alias y plan</button>
                </p>
                <h2>Uso</h2>
                <ActivityChart tenantId={ficha.tenantId} />
                <h2>Cuentas</h2>
                <p className="ficha-count"><strong>{ficha.loginCount ?? 0}</strong> cuentas de acceso, entre el administrador y los puntos de alerta.</p>
                <h2>Dispositivos con aviso</h2>
                <p className="ficha-count"><strong>{ficha.pushDeviceCount ?? 0}</strong> dispositivos con aviso registrados en este comercio.</p>
                <h2>Facturas</h2>
                <BillingOrders orders={fichaOrders} loading={!fichaOrdersLoaded} caption="Órdenes de este comercio" />
              </>
            ) : null}
          </>
        ) : null}
        {section === "facturaciones" ? (
          <>
            <h1>Facturaciones</h1>
            <p className="gate-lead">Cobros del abono en la cuenta de la plataforma.</p>
            <label>
              Cliente
              <select value={tenantFilter} onChange={(event) => setTenantFilter(event.target.value)}>
                <option value="">Todos</option>
                {clients.map((client) => (
                  <option key={client.tenantId} value={client.tenantId}>
                    {client.email || client.alias || client.tenantId}
                  </option>
                ))}
              </select>
            </label>
            {billingLoaded && billing.length === 0 ? <p>Todavía no hay cobros.</p> : null}
            <div className="owner-table-wrap" tabIndex={0} role="region" aria-label="Facturaciones">
            <table className="owner-table">
              <thead>
                <tr>
                  <th>Cliente</th>
                  <th>Plan</th>
                  <th className="num">Importe</th>
                  <th>Alta</th>
                  <th>Pago</th>
                  <th>Activación</th>
                  <th>Vencimiento</th>
                  <th>Adicional</th>
                </tr>
              </thead>
              <tbody>
                {billing.map((order) => (
                  <tr key={order.id}>
                    <td className="owner-email">{order.email || order.alias || "—"}</td>
                    <td><span className="plan-badge">{planFace(order.planCode)}</span></td>
                    <td className="money num" data-amount={order.amount}>
                      {money.format(order.amount)}
                    </td>
                    <td className="stamp">{stamp(order.createdAt)}</td>
                    <td className="stamp">{stamp(order.paidAt)}</td>
                    <td className="stamp">{stamp(order.activatedAt)}</td>
                    <td className="stamp">{stamp(order.targetAccessUntil)}</td>
                    <td>{order.additionalPoint ? "Sí" : "No"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            </div>
          </>
        ) : null}
        {section === "movimientos" ? (
          <>
            <h1>Actividad de tu cuenta Mercado Pago</h1>
            <p className="gate-lead">
              Todos los movimientos de tu cuenta de Mercado Pago (sin restricciones de tipo de medio, transferencias recibidas, cobros con QR, tarjetas, pases e ingresos por CVU). Tocá cualquier fila para abrir el comprobante completo.
            </p>
            <form
              className="owner-filters"
              onSubmit={(event) => {
                event.preventDefault();
                void loadMovements(0);
              }}
            >
              <label>
                Desde
                <input type="date" value={movFrom} onChange={(e) => setMovFrom(e.target.value)} />
              </label>
              <label>
                Hasta
                <input type="date" value={movTo} onChange={(e) => setMovTo(e.target.value)} />
              </label>
              <label>
                Estado
                <select value={movStatus} onChange={(e) => setMovStatus(e.target.value)}>
                  <option value="">Todos</option>
                  <option value="approved">Aprobados / Acreditados</option>
                  <option value="pending">Pendientes</option>
                  <option value="rejected">Rechazados</option>
                </select>
              </label>
              <label>
                Tipo de movimiento
                <select value={movType} onChange={(e) => setMovType(e.target.value)}>
                  <option value="">Todos los tipos</option>
                  <option value="account_fund">Ingreso CVU / Transferencia bancaria (Coelsa)</option>
                  <option value="money_transfer">Envío entre cuentas Mercado Pago</option>
                  <option value="regular_payment">Cobro regular / Checkout / Link</option>
                  <option value="pos_payment">Cobro QR / POS</option>
                </select>
              </label>
              <button type="submit" disabled={movementsLoading}>
                {movementsLoading ? "Consultando..." : "Consultar actividad"}
              </button>
            </form>
            {movementsError ? <p className="error" role="alert">{movementsError}</p> : null}
            <p className="money">
              {movements.length} movimientos en esta página · Total acreditado visible:{" "}
              {money.format(movements.filter((m) => m.status === "approved").reduce((acc, m) => acc + m.amount, 0))}
            </p>
            <div className="owner-table-wrap" tabIndex={0} role="region" aria-label="Actividad de Mercado Pago">
              <table className="owner-table owner-movements-table">
                <thead>
                  <tr>
                    <th>Fecha</th>
                    <th>N.° Operación</th>
                    <th>Origen / Contraparte</th>
                    <th>Tipo / Medio</th>
                    <th>Código COELSA / Ref</th>
                    <th className="num">Importe</th>
                    <th>Estado</th>
                    <th>Comprobante</th>
                  </tr>
                </thead>
                <tbody>
                  {movementsLoaded && movements.length === 0 ? (
                    <EmptyState colSpan={8} text="No hay movimientos para mostrar con estos filtros." />
                  ) : null}
                  {movements.map((mov) => (
                    <tr key={mov.id} className="clickable-row" onClick={() => setSelectedMovement(mov)}>
                      <td className="stamp">{stamp(mov.occurredAt)}</td>
                      <td className="mono">{mov.externalId}</td>
                      <td>
                        <strong>{mov.payerName}</strong>
                        {mov.payerDocument ? <div className="sub-meta mono">{mov.payerDocument}</div> : null}
                        {mov.payerEmail ? <div className="sub-meta">{mov.payerEmail}</div> : null}
                      </td>
                      <td>
                        {mov.operationLabel || mov.typeCode || "—"}
                        {mov.description ? <div className="sub-meta">{mov.description}</div> : null}
                      </td>
                      <td className="mono">{mov.coelsaId || mov.bankTransferId || "—"}</td>
                      <td className="money num">{money.format(mov.amount)}</td>
                      <td>
                        <span className={`status-pill ${mov.status}`}>
                          {mov.status === "approved" ? "✓ Acreditado" : mov.statusLabel}
                        </span>
                      </td>
                      <td>
                        <button
                          type="button"
                          className="secondary small"
                          onClick={(e) => {
                            e.stopPropagation();
                            setSelectedMovement(mov);
                          }}
                        >
                          Ver detalle
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="hero-actions" style={{ marginTop: "16px" }}>
              <button
                type="button"
                className="secondary"
                disabled={movementsLoading || movOffset === 0}
                onClick={() => void loadMovements(Math.max(0, movOffset - 30))}
              >
                Página anterior
              </button>
              <button
                type="button"
                className="secondary"
                disabled={movementsLoading || movNext === null}
                onClick={() => movNext !== null && void loadMovements(movNext)}
              >
                Página siguiente
              </button>
            </div>
            <dialog
              ref={movementDialog}
              className="transfer-detail"
              closedby="any"
              aria-labelledby="owner-movement-detail-title"
              onClose={() => setSelectedMovement(null)}
            >
              {selectedMovement ? (
                <div className="receipt-card">
                  <div className="receipt-header">
                    <h2 id="owner-movement-detail-title">Comprobante de Operación</h2>
                    <span className={`status-pill ${selectedMovement.status}`}>
                      {selectedMovement.status === "approved" ? "✓ Acreditado" : selectedMovement.statusLabel}
                    </span>
                  </div>
                  <p className="receipt-amount money">{money.format(selectedMovement.amount)}</p>
                  <p className="receipt-payer">{selectedMovement.payerName}</p>
                  <p className="receipt-when">{stamp(selectedMovement.occurredAt)}</p>
                  <dl className="receipt-grid">
                    <div className="receipt-row">
                      <dt>N.° de operación MP</dt>
                      <dd className="mono">{selectedMovement.externalId}</dd>
                    </div>
                    {selectedMovement.coelsaId ? (
                      <div className="receipt-row">
                        <dt>Código COELSA / E2E ID</dt>
                        <dd className="mono">{selectedMovement.coelsaId}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.bankTransferId ? (
                      <div className="receipt-row">
                        <dt>ID Transferencia bancaria</dt>
                        <dd className="mono">{selectedMovement.bankTransferId}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.operationLabel ? (
                      <div className="receipt-row">
                        <dt>Tipo de operación</dt>
                        <dd>{selectedMovement.operationLabel}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.typeCode ? (
                      <div className="receipt-row">
                        <dt>Medio de pago (`payment_type_id`)</dt>
                        <dd className="mono">{selectedMovement.typeCode}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.payerDocument ? (
                      <div className="receipt-row">
                        <dt>CUIT / CUIL / Documento</dt>
                        <dd className="mono">{selectedMovement.payerDocument}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.payerEmail ? (
                      <div className="receipt-row">
                        <dt>Correo del pagador</dt>
                        <dd>{selectedMovement.payerEmail}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.originBank ? (
                      <div className="receipt-row">
                        <dt>Banco / Billetera origen</dt>
                        <dd>{selectedMovement.originBank}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.originAccount ? (
                      <div className="receipt-row">
                        <dt>CBU / CVU origen</dt>
                        <dd className="mono">{selectedMovement.originAccount}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.description ? (
                      <div className="receipt-row">
                        <dt>Concepto / Descripción</dt>
                        <dd>{selectedMovement.description}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.statusDetailLabel ? (
                      <div className="receipt-row">
                        <dt>Detalle de estado</dt>
                        <dd>{selectedMovement.statusDetailLabel}</dd>
                      </div>
                    ) : null}
                    {selectedMovement.approvedAt ? (
                      <div className="receipt-row">
                        <dt>Fecha de acreditación</dt>
                        <dd>{stamp(selectedMovement.approvedAt)}</dd>
                      </div>
                    ) : null}
                  </dl>
                  <form method="dialog">
                    <button type="submit">Cerrar</button>
                  </form>
                </div>
              ) : null}
            </dialog>
          </>
        ) : null}
        {section === "planes" ? (
          <>
            <h1>Planes</h1>
            <p className="gate-lead">Editá precio, cupo de Puntos de Alerta y textos. La pantalla de Pagos solo lee los planes activos.</p>
            <ul className="owner-forms owner-plan-grid">
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
                      Importe (ARS)
                      <span className="owner-amount-field">
                        <span className="owner-currency" aria-hidden="true">$</span>
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
                      </span>
                    </label>
                    <label>
                      Puntos de Alerta incluidos
                      <input
                        type="number"
                        min="1"
                        max="1000"
                        value={draft.includedAlertPoints}
                        onChange={(event) => setPlanDrafts((value) => ({ ...value, [plan.code]: { ...draft, includedAlertPoints: Number(event.target.value) } }))}
                      />
                    </label>
                    <label>
                      Texto de cara
                      <input
                        value={draft.faceCopy}
                        placeholder="Ej: Para el comercio que cobra todos los días."
                        onChange={(event) =>
                          setPlanDrafts((value) => ({ ...value, [plan.code]: { ...draft, faceCopy: event.target.value } }))
                        }
                      />
                    </label>
                    <div className="checks">
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
                        Ayuda del copiloto
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
                    </div>
                    <button type="button" onClick={() => void savePlan(plan.code)}>
                      Guardar plan
                    </button>
                  </li>
                );
              })}
            </ul>
          </>
        ) : null}
        {section === "servicios" ? (
          <>
            <h1>Panel de servicios</h1>
            <p className="gate-lead">Cada tarjeta es un servicio que el producto ya usa. El valor completo no se muestra.</p>
            <ul className="owner-forms">
              {cards.map((card) => (
                <li key={card.card} className="plan">
                  <div className="owner-service-head">
                    <strong>{ownerCardLabels[card.card] ?? card.card}</strong>
                    <span className={card.isSet ? "owner-status is-set" : "owner-status"}>
                      {card.isSet
                        ? card.lastFour
                          ? `● Configurado · ···${card.lastFour}`
                          : "● Configurado"
                        : "Sin configurar"}
                    </span>
                  </div>
                  {card.card !== "whatsapp" ? (
                    <>
                      <label>
                        Reemplazar secreto
                        <input
                          type="password"
                          autoComplete="off"
                          placeholder="Pegá el nuevo secreto para reemplazar el actual..."
                          value={drafts[card.card] ?? ""}
                          onChange={(event) => setDrafts((value) => ({ ...value, [card.card]: event.target.value }))}
                        />
                      </label>
                      <button type="button" onClick={() => void replaceSecret(card.card)}>
                        Guardar
                      </button>
                    </>
                  ) : (
                    <div className="owner-wa-note">Solo wa.me · sin clave</div>
                  )}
                </li>
              ))}
            </ul>
          </>
        ) : null}
        <dialog
          ref={editDialog}
          className="transfer-detail"
          closedby="any"
          aria-labelledby="owner-edit-client-title"
          onClose={() => setEditing(null)}
        >
          {editing ? (
            <form onSubmit={(event) => void saveClient(event)}>
              <h2 id="owner-edit-client-title">Editar {editing.email || (editing.alias ? `#${editing.alias}` : "comercio")}</h2>
              <p className="gate-lead">Alias y plan. No hay notas ni baja de comercios.</p>
              <label>
                Alias
                <input value={editAlias} onChange={(event) => setEditAlias(event.target.value)} required autoComplete="off" />
              </label>
              <label>
                Plan
                <select value={editPlan} onChange={(event) => setEditPlan(event.target.value)}>
                  <option value="pack_mes">Pack Mes</option>
                  <option value="pack_evento">Pack Evento</option>
                  <option value="pack_corporativo">Pack Corporativo</option>
                </select>
              </label>
              {editError ? <p className="error" role="alert">{editError}</p> : null}
              <div className="hero-actions">
                <button type="submit" disabled={editSaving}>{editSaving ? "Guardando…" : "Guardar"}</button>
                <button type="button" className="secondary" onClick={() => setEditing(null)}>Cancelar</button>
              </div>
            </form>
          ) : null}
        </dialog>
        </div>
      </div>
    </div>
  );
}
