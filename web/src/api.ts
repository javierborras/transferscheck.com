const origin = import.meta.env.VITE_API_ORIGIN ?? "http://localhost:5080";

export async function api(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers);
  if (init.body && !headers.has("Content-Type"))
    headers.set("Content-Type", "application/json");
  return fetch(`${origin}${path}`, { ...init, headers, credentials: "include" });
}
