self.addEventListener("install", (event) => {
  event.waitUntil(self.skipWaiting());
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("push", (event) => {
  event.waitUntil((async () => {
    let payload;
    try { payload = event.data ? event.data.json() : {}; } catch { return; }
    const data = payload.data || {};
    const first = String(payload.transferId || data.transferId || "");
    if (!first) return;
    const clients = await self.clients.matchAll({ type: "window", includeUncontrolled: false });
    const visible = clients.find(client => client.visibilityState === "visible" && client.focused) || clients.find(client => client.visibilityState === "visible");
    if (visible) {
      visible.postMessage({ type: "transfer-arrived", transferId: first });
      return;
    }
    await notifyPayment(first, payload.notification?.title || data.title || "Entró un pago", payload.notification?.body || data.body || `${data.payerName || ""} · ${data.amount || ""}`);
  })());
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const first = event.notification.data?.first || "";
  const target = first ? `/?first=${encodeURIComponent(first)}` : "/";
  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: false });
    const existing = windows.find(client => new URL(client.url).origin === self.location.origin);
    if (existing) { await existing.navigate(target); await existing.focus(); }
    else await self.clients.openWindow(target);
  })());
});

// Serialize notification claims so a page fallback and a push cannot both notify.
let notifications = Promise.resolve();
function notifyPayment(first, title, body) {
  const result = notifications.then(async () => {
    const cache = await caches.open("payment-notifications-v1");
    const key = new URL(`/__notification/${encodeURIComponent(first)}`, self.location.origin).href;
    if (await cache.match(key)) return;

    await self.registration.showNotification(title, { body, tag: `payment-${first}`, silent: false, data: { first } });
    await cache.put(key, new Response("shown"));
    const keys = await cache.keys();
    await Promise.all(keys.slice(0, Math.max(0, keys.length - 500)).map(request => cache.delete(request)));
  });
  notifications = result.catch(() => {});
  return result;
}
self.addEventListener("message", event => {
  if (event.data?.type === "notify-payment" && event.data.first && event.source?.url && new URL(event.source.url).origin === self.location.origin)
    event.waitUntil(notifyPayment(String(event.data.first), "Entró un pago", String(event.data.body || "")));
});
