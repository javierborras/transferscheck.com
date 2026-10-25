import { Link } from "react-router-dom";
export type Plan = { code: string; displayName: string; amount: number; currency: string; durationDays?: number | null; durationHours?: number | null; faceCopy: string; enableCopilotHelp: boolean };
export function PlanCards({ plans, initial = false, busy = false, onPay }: { plans: Plan[]; initial?: boolean; busy?: boolean; onPay?: (code: string) => void }) {
  return <div className="plan-grid">{plans.map(plan => {
    const monthly = plan.code === "pack_mes";
    const duration = plan.durationHours ? `${plan.durationHours} horas` : `${plan.durationDays} días`;
    const benefits = initial
      ? monthly ? ["1 cuenta Dueño (Admin) + hasta 4 Puntos de Alerta en mostrador", "Alertas con sonido y notificaciones al instante", "Historial completo y descarga de planilla para el dueño", "500 consultas manuales por semana compartidas"]
        : [`Acceso completo por ${plan.durationHours ?? (plan.durationDays ?? 0) * 24} hs corridas`, "Pensado para usar en el teléfono o barra", "Sin suscripción ni renovación automática"]
      : monthly ? ["Tus 4 mostradores vuelven a sonar al instante", "Historial completo y descarga de planilla para Excel", ...(plan.enableCopilotHelp ? ["Ayuda con Copiloto inteligente incluida"] : [])]
        : ["Ideal para controlar pagos en la barra o entrada desde el teléfono", "Sonido y aviso instantáneo de transferencias", "Sin renovación ni compromiso mensual"];
    return <article key={plan.code} className={`andes-card commercial-plan ${monthly ? "recommended" : ""}`}>
      <div><span className={`plan-badge ${monthly && initial ? "promo" : ""}`}>{monthly ? initial ? "🎁 21 DÍAS DE PRUEBA GRATIS" : "RECOMENDADO PARA LOCALES" : initial ? `Pase por ${duration}` : "USO EVENTUAL"}</span>
        <h2>{plan.displayName}</h2>
        <p>{initial ? monthly ? "Para el comercio que cobra todos los días." : "Un día, un usuario. Ideal para la fiesta, feria o show." : plan.faceCopy}</p>
        <div className="plan-price"><strong className="money">{new Intl.NumberFormat("es-AR", { style: "currency", currency: plan.currency }).format(plan.amount)}</strong><span>{duration}</span><p className="payment-hint">{monthly ? "Pago por link oficial de Mercado Pago • Reactivación inmediata" : `Habilita alertas durante ${duration} corridas`}</p></div>
        <ul>{benefits.map(text => <li key={text}>✓ {text}</li>)}</ul>
      </div>
      {onPay ? <button type="button" disabled={busy} className={monthly ? "" : "secondary"} onClick={() => onPay(plan.code)}>{busy ? "Abriendo el pago..." : monthly ? "Pagar Pack Mes con Mercado Pago" : `Pagar Pack Evento (${plan.durationHours ?? (plan.durationDays ?? 0) * 24} hs)`}</button> : <Link className={`button ${monthly ? "" : "secondary"}`} to={`/cuenta?plan=${encodeURIComponent(plan.code)}`}>{monthly ? "Empezar prueba de 21 días gratis" : "Elegir Pack Evento"}</Link>}
    </article>;
  })}</div>;
}
