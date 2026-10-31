const money = new Intl.NumberFormat("es-AR", { style: "currency", currency: "ARS", maximumFractionDigits: 2 });
const when = new Intl.DateTimeFormat("es-AR", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false
});

export type BillingOrder = {
  id: string;
  tenantId: string;
  alias: string | null;
  email: string;
  planCode: string;
  amount: number;
  currency: string;
  createdAt: string;
  paidAt: string | null;
  activatedAt: string | null;
  targetAccessUntil: string | null;
  additionalPoint: boolean;
};

function stamp(value: string | null) {
  return value ? when.format(new Date(value)) : "—";
}

function planFace(code: string | null | undefined) {
  if (code === "pack_mes") return "Pack Mes";
  if (code === "pack_evento") return "Pack Evento";
  return code || "—";
}

export function BillingOrders({
  orders,
  loading,
  caption
}: {
  orders: BillingOrder[];
  loading?: boolean;
  caption?: string;
}) {
  return (
    <div className="client-orders-box">
      {caption ? <strong>{caption}</strong> : null}
      {loading ? <p>Cargando facturas…</p> : null}
      {!loading && orders.length === 0 ? <p>Este comercio todavía no registra órdenes de pago.</p> : null}
      {!loading && orders.length > 0 ? (
        <table className="owner-subtable">
          <thead>
            <tr>
              <th>Plan</th>
              <th className="num">Importe</th>
              <th>Alta</th>
              <th>Pago</th>
              <th>Activación</th>
              <th>Vencimiento</th>
              <th>Estado</th>
            </tr>
          </thead>
          <tbody>
            {orders.map((order) => (
              <tr key={order.id}>
                <td>
                  <span className="plan-badge">
                    {order.additionalPoint ? `${planFace(order.planCode)} (Punto adicional)` : planFace(order.planCode)}
                  </span>
                </td>
                <td className="money num">{money.format(order.amount)}</td>
                <td className="stamp">{stamp(order.createdAt)}</td>
                <td className="stamp">{stamp(order.paidAt)}</td>
                <td className="stamp">{stamp(order.activatedAt)}</td>
                <td className="stamp">{stamp(order.targetAccessUntil)}</td>
                <td>
                  <span className={`status-pill ${order.paidAt ? "approved" : "pending"}`}>
                    {order.paidAt ? "Pagado" : "Pendiente"}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}
