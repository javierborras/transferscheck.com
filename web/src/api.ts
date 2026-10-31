export const apiOrigin = import.meta.env.VITE_API_ORIGIN ?? (import.meta.env.DEV ? "http://localhost:5080" : "");

const correlationHeader = "X-Correlation-ID";
const sent = new WeakMap<Response, string>();

export function apiUrl(path: string) {
  return `${apiOrigin}${path}`;
}

export function createCorrelationId() {
  const cryptoApi = globalThis.crypto;
  if (cryptoApi && typeof cryptoApi.randomUUID === "function") return cryptoApi.randomUUID();
  return `tc${Date.now().toString(16)}${Math.random().toString(16).slice(2, 12)}`;
}

function readCorrelation(response: Response) {
  return response.headers.get(correlationHeader) || sent.get(response) || "";
}

export function attachCorrelation(error: Error, response: Response) {
  if (response.status < 500) return error;
  const id = readCorrelation(response);
  if (id) (error as Error & { correlationId?: string }).correlationId = id;
  return error;
}

export function withCorrelation(source: Response | unknown, message: string) {
  if (source instanceof Response && source.status < 500) return message;
  const id = source instanceof Response
    ? readCorrelation(source)
    : source instanceof Error
      ? (source as Error & { correlationId?: string }).correlationId ?? ""
      : "";
  return id && /^[A-Za-z0-9_-]{8,64}$/.test(id) ? `${message} Referencia: ${id}.` : message;
}

export async function api(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers);
  if (init.body && !headers.has("Content-Type"))
    headers.set("Content-Type", "application/json");
  const correlationId = headers.get(correlationHeader) || createCorrelationId();
  headers.set(correlationHeader, correlationId);
  try {
    const response = await fetch(apiUrl(path), { ...init, headers, credentials: "include" });
    sent.set(response, response.headers.get(correlationHeader) || correlationId);
    return response;
  } catch (error) {
    if (error instanceof Error) (error as Error & { correlationId?: string }).correlationId = correlationId;
    throw error;
  }
}
