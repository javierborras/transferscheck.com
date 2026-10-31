import { useEffect, useId, useState } from "react";
import { api, withCorrelation } from "../api";

type Point = { date: string; signups: number; queries: number };
type Activity = {
  days: number;
  showSignups: boolean;
  callsRecordedFrom: string | null;
  points: Point[];
};

const widths = { view: 720, height: 240, left: 44, right: 16, top: 16, bottom: 32 };

function dayLabel(iso: string) {
  const [year, month, day] = iso.slice(0, 10).split("-");
  if (!year || !month || !day) return iso;
  return `${day}/${month}`;
}

function fullDay(iso: string) {
  const [year, month, day] = iso.slice(0, 10).split("-");
  if (!year || !month || !day) return iso;
  return `${day}/${month}/${year}`;
}

function activityNote(from: string | null) {
  if (!from) return "Las consultas a Mercado Pago se anotan desde ahora. Los días anteriores quedan en cero.";
  return `Las consultas a Mercado Pago se anotan desde el ${fullDay(from)}. Los días anteriores quedan en cero.`;
}

function xAt(index: number, count: number) {
  const plot = widths.view - widths.left - widths.right;
  if (count <= 1) return widths.left + plot / 2;
  return widths.left + (index / (count - 1)) * plot;
}

function line(points: Point[], key: "signups" | "queries", max: number) {
  const plot = widths.height - widths.top - widths.bottom;
  return points
    .map((point, index) => {
      const y = widths.top + plot - (point[key] / max) * plot;
      return `${xAt(index, points.length)},${y}`;
    })
    .join(" ");
}

export function ActivityChart({ tenantId }: { tenantId?: string }) {
  const titleId = useId();
  const [days, setDays] = useState<7 | 30 | 90>(30);
  const [activity, setActivity] = useState<Activity | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let gone = false;
    setError("");
    void (async () => {
      const query = new URLSearchParams({ days: String(days) });
      if (tenantId) query.set("tenantId", tenantId);
      try {
        const response = await api(`/api/owner/activity?${query}`);
        if (gone) return;
        if (!response.ok) {
          setActivity(null);
          setError(withCorrelation(response, "No se pudo cargar la actividad."));
          return;
        }
        setActivity((await response.json()) as Activity);
      } catch (caught) {
        if (gone) return;
        setActivity(null);
        setError(withCorrelation(caught, "No se pudo cargar la actividad."));
      }
    })();
    return () => {
      gone = true;
    };
  }, [days, tenantId]);

  const points = activity?.points ?? [];
  const showSignups = activity?.showSignups !== false && !tenantId ? true : Boolean(activity?.showSignups);
  const signupTotal = points.reduce((sum, point) => sum + point.signups, 0);
  const queryTotal = points.reduce((sum, point) => sum + point.queries, 0);
  const visibleMax = Math.max(1, ...points.map((point) => (showSignups ? Math.max(point.signups, point.queries) : point.queries)));
  const plotBottom = widths.height - widths.bottom;
  const signupName = tenantId ? "Alta" : "Altas";

  return (
    <section className="activity-panel" aria-labelledby={titleId}>
      <div className="activity-toolbar">
        <h3 id={titleId}>{tenantId ? "Consultas a Mercado Pago" : "Altas y consultas"}</h3>
        <div className="activity-range" role="group" aria-label="Período">
          {([7, 30, 90] as const).map((option) => (
            <button
              key={option}
              type="button"
              className="secondary small"
              aria-pressed={days === option}
              onClick={() => setDays(option)}
            >
              {option} días
            </button>
          ))}
        </div>
      </div>
      {error ? <p className="error" role="alert">{error}</p> : null}
      {points.length > 0 ? (
        <>
          <svg className="activity-chart" viewBox={`0 0 ${widths.view} ${widths.height}`} role="img" aria-labelledby={titleId}>
            <line x1={widths.left} y1={widths.top} x2={widths.left} y2={plotBottom} className="activity-axis" />
            <line x1={widths.left} y1={plotBottom} x2={widths.view - widths.right} y2={plotBottom} className="activity-axis" />
            <text x={widths.left - 8} y={widths.top + 4} textAnchor="end" className="activity-tick">{visibleMax}</text>
            <text x={widths.left - 8} y={plotBottom} textAnchor="end" className="activity-tick">0</text>
            {showSignups ? <polyline points={line(points, "signups", visibleMax)} className="activity-line signups" /> : null}
            <polyline points={line(points, "queries", visibleMax)} className="activity-line queries" />
            {points.map((point, index) => {
              const plot = widths.height - widths.top - widths.bottom;
              const x = xAt(index, points.length);
              return (
                <g key={point.date}>
                  {showSignups && point.signups > 0 ? (
                    <circle cx={x} cy={widths.top + plot - (point.signups / visibleMax) * plot} r="3.5" className="activity-dot signups" />
                  ) : null}
                  {point.queries > 0 ? (
                    <circle cx={x} cy={widths.top + plot - (point.queries / visibleMax) * plot} r="3.5" className="activity-dot queries" />
                  ) : null}
                </g>
              );
            })}
            <text x={xAt(0, points.length)} y={widths.height - 8} textAnchor="middle" className="activity-tick">{dayLabel(points[0].date)}</text>
            {points.length > 2 ? (
              <text x={xAt(Math.floor((points.length - 1) / 2), points.length)} y={widths.height - 8} textAnchor="middle" className="activity-tick">
                {dayLabel(points[Math.floor((points.length - 1) / 2)].date)}
              </text>
            ) : null}
            <text x={xAt(points.length - 1, points.length)} y={widths.height - 8} textAnchor="middle" className="activity-tick">
              {dayLabel(points[points.length - 1].date)}
            </text>
          </svg>
          <div className="activity-legend">
            {showSignups ? (
              <span><i className="activity-swatch signups" aria-hidden="true" /> {signupName}</span>
            ) : null}
            <span><i className="activity-swatch queries" aria-hidden="true" /> Consultas a Mercado Pago</span>
          </div>
          <p className="activity-totals">
            {showSignups ? <span>{signupName} en el período: <strong>{signupTotal}</strong></span> : null}
            <span>Consultas a Mercado Pago en el período: <strong>{queryTotal}</strong></span>
          </p>
          <table className="activity-data">
            <caption>Datos del gráfico</caption>
            <thead>
              <tr>
                <th>Día</th>
                {showSignups ? <th>{signupName}</th> : null}
                <th>Consultas a Mercado Pago</th>
              </tr>
            </thead>
            <tbody>
              {points.map((point) => (
                <tr key={point.date}>
                  <td>{fullDay(point.date)}</td>
                  {showSignups ? <td>{point.signups}</td> : null}
                  <td>{point.queries}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      ) : error ? null : <p>Cargando actividad…</p>}
      {activity ? <p className="activity-note">{activityNote(activity.callsRecordedFrom)}</p> : null}
    </section>
  );
}
