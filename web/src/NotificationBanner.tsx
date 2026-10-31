import { useEffect, useState } from "react";
import { api, withCorrelation } from "./api";
import { enableDeviceAlerts } from "./push";

type Status = "checking" | "active" | "off" | "blocked" | "unavailable";

export function NotificationBanner({ accountId }: { accountId: string }) {
  const [status, setStatus] = useState<Status>("checking");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let gone = false;
    async function check() {
      let next: Status = "off";
      try {
        if (!("Notification" in window) || !("serviceWorker" in navigator) || !("PushManager" in window)) next = "unavailable";
        else if (Notification.permission === "denied") next = "blocked";
        else {
          const response = await api("/api/push/config");
          if (!response.ok) throw new Error();
          const config = await response.json();
          const configured = Boolean(config.apiKey && config.projectId && config.appId && config.messagingSenderId && config.vapidKey)
            || (import.meta.env.DEV && import.meta.env.VITE_ENABLE_MOCK_SERVICES === "true");
          if (!configured) next = "unavailable";
          else if (Notification.permission === "granted" && localStorage.getItem("push-account") === accountId) {
            const registration = await navigator.serviceWorker.getRegistration();
            const subscription = await registration?.pushManager.getSubscription();
            if (subscription || (import.meta.env.DEV && import.meta.env.VITE_ENABLE_MOCK_SERVICES === "true")) next = "active";
          }
        }
      } catch { next = "off"; }
      if (!gone) setStatus(next);
    }
    const visible = () => { if (document.visibilityState === "visible") void check(); };
    void check();
    window.addEventListener("focus", visible);
    window.addEventListener("storage", visible);
    document.addEventListener("visibilitychange", visible);
    return () => {
      gone = true;
      window.removeEventListener("focus", visible);
      window.removeEventListener("storage", visible);
      document.removeEventListener("visibilitychange", visible);
    };
  }, [accountId, revision]);

  async function enable() {
    setBusy(true); setError("");
    try { await enableDeviceAlerts(); }
    catch (error) { setError(withCorrelation(error, "No pudimos activar las notificaciones. Revisá los permisos de este navegador y volvé a intentar.")); }
    finally { setBusy(false); setRevision(value => value + 1); }
  }

  if (status === "checking" || status === "active") return null;
  return <section className="notification-banner" role="status" aria-label="Estado de las notificaciones">
    <div><strong>🔔 Las notificaciones no están activadas en este dispositivo</strong>
      <p>{status === "blocked" ? "Tu navegador las bloqueó. Habilitalas desde los permisos de este sitio y volvé a la página."
        : status === "unavailable" ? "Por ahora no podemos enviar avisos a este dispositivo. Mantené el mostrador abierto y activá la campanita para escuchar los cobros."
        : "Activá los avisos para recibir una notificación cuando detectemos un pago. En el iPhone, agregá la app a la pantalla de inicio."}</p>
      {error ? <p role="alert">{error}</p> : null}</div>
    {status === "off" ? <button className="secondary" disabled={busy} onClick={() => void enable()}>{busy ? "Activando…" : "Activar notificaciones"}</button> : null}
  </section>;
}
