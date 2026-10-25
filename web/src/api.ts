export const apiOrigin = import.meta.env.VITE_API_ORIGIN ?? (import.meta.env.DEV ? "http://localhost:5080" : "");

export function apiUrl(path: string) {
  return `${apiOrigin}${path}`;
}

export async function api(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers);
  if (init.body && !headers.has("Content-Type"))
    headers.set("Content-Type", "application/json");
  return fetch(apiUrl(path), { ...init, headers, credentials: "include" });
}
