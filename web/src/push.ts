import { api } from "./api";

export async function enableDeviceAlerts() {
  if (!("Notification" in window) || !("serviceWorker" in navigator)) throw new Error();
  const permission = Notification.permission === "default" ? await Notification.requestPermission() : Notification.permission;
  if (permission !== "granted") throw new Error();
  const response = await api("/api/push/config");
  if (!response.ok) throw new Error();
  const config = await response.json();
  let token: string;
  if (config.apiKey) {
    const [{ initializeApp, getApps }, { getMessaging, getToken, isSupported }] = await Promise.all([import("@firebase/app"), import("@firebase/messaging")]);
    if (!(await isSupported())) throw new Error();
    const app = getApps()[0] ?? initializeApp(config);
    const registration = await navigator.serviceWorker.ready;
    token = await getToken(getMessaging(app), { vapidKey: config.vapidKey, serviceWorkerRegistration: registration });
  } else if (import.meta.env.DEV && config.projectId === "mock") {
    token = `mock-${crypto.randomUUID()}`;
  } else throw new Error();
  if (!token) throw new Error();
  const saved = await api("/api/push/tokens", { method: "POST", body: JSON.stringify({ token }) });
  if (!saved.ok) throw new Error();
}
