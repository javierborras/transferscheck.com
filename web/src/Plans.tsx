import { Link } from "react-router-dom";
export type Plan = { code: string; displayName: string; amount: number; currency: string; durationDays?: number | null; durationHours?: number | null; faceCopy: string; enableCopilotHelp: boolean; includedAlertPoints?: number; additionalPointPrice?: number; renewalAmount?: number; renewingPoints?: number };
export function PlanCards({ plans, initial = false, busy = false, primary = true, onPay }: { plans: Plan[]; initial?: boolean; busy?: boolean; primary?: boolean; onPay?: (code: string) => void }) {
  return <div className="plan-grid">{plans.map(plan => {
    const monthly = plan.code === "pack_mes";
    const duration = plan.durationHours ? `${plan.durationHours} horas` : `${plan.durationDays} días`;
    const included = plan.includedAlertPoints ?? (monthly ? 4 : 1);
    const extraPrice = plan.additionalPointPrice ?? 1000;
    const benefits = [
      `1 Admin + ${included} Punto${included > 1 ? "s" : ""} de Alerta incluido${included > 1 ? "s" : ""}`,
      "Actualización automática y revisión manual",
      "Sonido con la pantalla abierta y activada; avisos del navegador según permisos",
      `Cada Punto de Alerta adicional: ${new Intl.NumberFormat("es-AR", { style: "currency", currency: plan.currency }).format(extraPrice)}, hasta el vencimiento del Admin`,
      monthly ? "Renovación prepaga, sin débito automático" : "El pase empieza cuando tocás Iniciar Evento"
    ];
    return <article key={plan.code} className={`andes-card commercial-plan ${monthly ? "recommended" : ""}`}>
      <div><span className={`plan-badge ${monthly && initial ? "promo" : ""}`}>{monthly ? initial ? "🎁 21 DÍAS DE PRUEBA GRATIS" : "RECOMENDADO PARA LOCALES" : initial ? `Pase por ${duration}` : "USO EVENTUAL"}</span>
        <h2>{plan.displayName}</h2>
        <p>{initial ? monthly ? "Para el comercio que cobra todos los días." : "Un Admin + un Punto de Alerta. Ideal para la fiesta, feria o show." : plan.faceCopy}</p>
        <div className="plan-price"><strong className="money">{new Intl.NumberFormat("es-AR", { style: "currency", currency: plan.currency }).format(plan.amount)}</strong><span>{duration}</span><p className="payment-hint">{monthly ? "Pago prepago por Mercado Pago • Sin débito automático" : `Habilita alertas durante ${duration} corridas`}</p></div>
        {onPay && (plan.renewingPoints ?? 0) > 0 ? <p className="money">Renovación: {plan.renewingPoints} adicional(es) · Total {new Intl.NumberFormat("es-AR", { style: "currency", currency: plan.currency }).format(plan.renewalAmount ?? plan.amount)}</p> : null}
        <ul>{benefits.map(text => <li key={text}>✓ {text}</li>)}</ul>
      </div>
      {onPay ? <button type="button" disabled={busy} className={monthly && primary ? "" : "secondary"} onClick={() => onPay(plan.code)}>{busy ? "Abriendo el pago..." : monthly ? "Renovar Pack Mes con Mercado Pago" : `Comprar Individual (${plan.durationHours ?? (plan.durationDays ?? 0) * 24} hs)`}</button> : <Link className={`button ${monthly ? "" : "secondary"}`} to={`/cuenta?plan=${encodeURIComponent(plan.code)}`}>{monthly ? "Empezar prueba de 21 días gratis" : "Elegir Individual / Evento"}</Link>}
    </article>;
  })}</div>;
}
