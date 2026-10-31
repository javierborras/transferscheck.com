import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "./App";
import "./styles.css";

// Keep the session and API origin policy on the canonical public hostname.
if (!import.meta.env.DEV && window.location.hostname === "www.transferscheck.com") {
  const canonical = new URL(window.location.href);
  canonical.hostname = "transferscheck.com";
  window.location.replace(canonical.href);
} else {
  if ("serviceWorker" in navigator)
    void navigator.serviceWorker.register("/sw.js").catch(() => undefined);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>
);
}
