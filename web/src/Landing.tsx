import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "./api";
import { Logo } from "./Logo";
import { PlanCards, type Plan } from "./Plans";
import { unlockSound } from "./audio";
export function Landing() {
  const [plans, setPlans] = useState<Plan[]>([]);
  const [error, setError] = useState("");
  useEffect(() => { let gone = false; api("/api/plans").then(async response => {
    if (!response.ok) throw new Error();
    const body = await response.json();
    if (!gone) setPlans(body.plans);
  }).catch(() => { if (!gone) setError("No pudimos cargar los planes. Volvé a intentar en un momento."); }); return () => { gone = true; }; }, []);
  return <div className="landing"><header className="public-header"><Link to="/" aria-label="Inicio"><Logo /></Link><nav><Link to="/ingresar">Ingresar</Link><Link className="button" to="/cuenta">Probar 21 días gratis</Link></nav></header>
    <main><section className="hero"><div><span className="promo plan-badge">⚡ Pensado para comercios y eventos en Argentina</span><h1>Que tu mostrador vea cuando entra una transferencia, sin darle acceso a tu Mercado Pago.</h1><p className="lead">Se acredita un pago → suena una alerta en la caja o barra → tu empleado verifica monto y nombre al instante. Sin compartir tu contraseña, sin mostrar tu saldo total y sin que te llamen por cada cobro.</p><div className="hero-actions"><Link className="button" to="/cuenta">Empezar prueba de 21 días gratis</Link><a className="button secondary" href="#planes">Ver planes y Pack Evento</a></div><p className="trust">🔒 Conexión oficial segura · 🙈 Modo ciego para cajeros · 📲 Instalable en PC y celular</p></div>
      <aside className="andes-card preview"><span className="demo-label">Vista de ejemplo del mostrador</span><p>✨ ÚLTIMO COBRO RECIBIDO</p><strong className="money">$ 18.500,00</strong><span className="approved-badge">✓ Acreditado</span><h2>Pagó: Juan Carlos Gómez</h2><p>Hace instantes</p><button className="secondary" onClick={() => void unlockSound().catch(() => setError("No pudimos activar el sonido. Probá de nuevo."))}>🔊 Probar cómo suena la alerta en caja</button></aside></section>
      <section><h2>¿Para quién es?</h2><div className="audience-grid">{[["🏪 Kioscos, Despensas y Almacenes", "Atendé rápido sin frenar la fila. El empleado confirma en 1 segundo que el pago entró de verdad y evita comprobantes falsos."], ["🍔 Bares, Restaurantes y Locales", "Habilitá hasta 4 pantallas de mostrador o cajas al mismo tiempo. Cada cajero ve solo las últimas 10 transferencias que entran."], ["🎪 Fiestas, Ferias, Shows y Kermesses", "¿Tenés un evento de un día? Activá el Pack Evento por 24 horas y controlá las transferencias en la entrada o la barra desde el celular."]].map(([title, text]) => <article className="andes-card" key={title}><h3>{title}</h3><p>{text}</p></article>)}</div></section>
      <section><h2>¿Cómo cuida tu cuenta?</h2><div className="privacy-grid"><article className="andes-card"><h3>Lo que ve el mostrador</h3><p>✅ Últimas 10 transferencias entrantes: Fecha, Nombre del pagador, Monto y Estado Acreditado</p></article><article className="andes-card"><h3>Lo que NUNCA ven tus empleados</h3><p>❌ Tu saldo total en Mercado Pago</p><p>❌ Transferencias salientes ni pagos a proveedores</p><p>❌ Historial viejo, descargas a Excel ni tu contraseña</p></article></div></section>
      <section id="planes"><h2>Elegí el plan para tu comercio o evento</h2>{error ? <p role="alert" className="error">{error}</p> : plans.length ? <PlanCards plans={plans} initial /> : <p role="status">Cargando planes...</p>}</section>
    </main><footer>transferscheck.com · Tus notificaciones en todas partes.</footer></div>;
}
