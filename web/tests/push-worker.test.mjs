import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

function worker(clients = []) {
  const listeners = new Map(), shown = [], entries = new Map();
  const cache = { match: async key => entries.get(key), put: async (key, value) => entries.set(key, value), keys: async () => [...entries.keys()], delete: async key => entries.delete(key) };
  const self = { location: { origin: "https://app.test" }, clients: { matchAll: async () => clients }, registration: { showNotification: async (title, options) => shown.push({ title, ...options }) }, addEventListener: (type, callback) => listeners.set(type, callback) };
  vm.runInNewContext(readFileSync(new URL("../public/sw.js", import.meta.url), "utf8"), { self, URL, Response, caches: { open: async () => cache } });
  return { shown, async emit(type, data) { let completed; listeners.get(type)({ ...data, waitUntil: promise => { completed = promise; } }); await completed; } };
}

test("a visible screen receives the payment without an OS notification", async () => {
  const messages = [];
  const w = worker([{ visibilityState: "visible", focused: true, postMessage: message => messages.push(message) }]);
  await w.emit("push", { data: { json: () => ({ data: { transferId: "payment-1" } }) } });
  assert.equal(messages[0].transferId, "payment-1"); assert.equal(w.shown.length, 0);
});

test("concurrent push and page fallback show a single OS notification", async () => {
  const w = worker();
  await Promise.all([
    w.emit("push", { data: { json: () => ({ data: { transferId: "payment-2", title: "Pago", body: "Persona · 100" } }) } }),
    w.emit("message", { data: { type: "notify-payment", first: "payment-2", body: "Persona · 100" }, source: { url: "https://app.test/" } })
  ]);
  assert.equal(w.shown.length, 1); assert.equal(w.shown[0].tag, "payment-payment-2");
  await w.emit("push", { data: { json: () => ({ data: { transferId: "payment-2" } }) } });
  assert.equal(w.shown.length, 1);
});

test("malformed push and foreign page messages do not notify", async () => {
  const w = worker();
  await w.emit("push", { data: { json: () => { throw new Error("invalid"); } } });
  await w.emit("message", { data: { type: "notify-payment", first: "foreign" }, source: { url: "https://other.test/" } });
  assert.equal(w.shown.length, 0);
});
