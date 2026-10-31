import { api, attachCorrelation } from "./api";

export async function enableDeviceAlerts() {
  if (!("Notification" in window) || !("serviceWorker" in navigator)) throw new Error();
  const permission = Notification.permission === "default" ? await Notification.requestPermission() : Notification.permission;
  if (permission !== "granted") throw new Error();
  const response = await api("/api/push/config");
  if (!response.ok) throw attachCorrelation(new Error("config"), response);
  const config = await response.json();
  let token: string;
  if (config.apiKey) {
    const [{ initializeApp, getApps }, { getMessaging, getToken, isSupported }] = await Promise.all([import("@firebase/app"), import("@firebase/messaging")]);
    if (!(await isSupported())) throw new Error();
    const app = getApps()[0] ?? initializeApp(config);
    const registration = await navigator.serviceWorker.ready;
    token = await getToken(getMessaging(app), { vapidKey: config.vapidKey, serviceWorkerRegistration: registration });
  } else if (import.meta.env.DEV && import.meta.env.VITE_ENABLE_MOCK_SERVICES === "true") {
    token = (await import("./development/mockPush")).mockPushToken(config.projectId);
  } else throw new Error();
  if (!token) throw new Error();
  const saved = await api("/api/push/tokens", { method: "POST", body: JSON.stringify({ token }) });
  if (!saved.ok) throw attachCorrelation(new Error("token"), saved);
  const session = await api("/api/session");
  if (session.ok) localStorage.setItem("push-account", (await session.json()).accountId);
}

// Optional OS notification while the page is open. Visual/audio continue without it.
export async function showDesktopFallback(id: string, payerName: string, amount: number) {
  if (!("Notification" in window) || Notification.permission !== "granted" || document.visibilityState === "visible") return;
  try {
    const registration = await navigator.serviceWorker.getRegistration();
    if (!registration?.active) return;
    registration.active.postMessage({ type: "notify-payment", first: id, title: "Entró un pago", body: `${payerName} · ${new Intl.NumberFormat("es-AR", { style: "currency", currency: "ARS" }).format(amount)}` });
    /* Notification delivery is deduplicated in the same worker as push. */
  } catch { /* The on-page notice is the fallback when the OS cannot display one. */ }
}

export async function claimPaymentNotice(scope: string, id: string): Promise<boolean> {
  const key = `payment-notice:${scope}:${id}`;
  const claim = () => {
    try {
      if (localStorage.getItem(key)) return false;
      localStorage.setItem(key, String(Date.now()));
      const old = Object.keys(localStorage).filter(item => item.startsWith("payment-notice:"));
      if (old.length > 500) old.sort((a, b) => Number(localStorage.getItem(a)) - Number(localStorage.getItem(b))).slice(0, old.length - 500).forEach(item => localStorage.removeItem(item));
    } catch { /* Private browsers may deny storage; per-view deduplication still applies. */ }
    return true;
  };
  return navigator.locks ? navigator.locks.request(key, claim) : claim();
}
