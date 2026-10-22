self.addEventListener("install", (event) => {
  event.waitUntil(self.skipWaiting());
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("push", (event) => {
  const payload = event.data ? event.data.json() : {};
  const title = payload.title || payload.notification?.title || "Llegó";
  const body = payload.body || payload.notification?.body || "";
  const first = payload.transferId || payload.data?.transferId || "";
  event.waitUntil(
    self.registration.showNotification(title, {
      body,
      silent: false,
      data: { first }
    })
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const first = event.notification.data?.first || "";
  const target = first ? `/?first=${encodeURIComponent(first)}` : "/";
  event.waitUntil(self.clients.openWindow(target));
});
