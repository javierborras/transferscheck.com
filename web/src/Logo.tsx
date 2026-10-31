export function Logo({ compact = false }: { compact?: boolean }) {
  const symbol = <g transform="translate(10, 12)">
      <rect x="4" y="4" width="44" height="44" rx="12" fill="#009EE3" />
      <path d="M15 26 H29 M23 20 L29 26 L23 32" fill="none" stroke="#FFE600" strokeWidth="3.5" strokeLinecap="round" strokeLinejoin="round" />
      <rect x="24" y="16" width="40" height="40" rx="12" fill="#00A650" stroke="#FFF" strokeWidth="3" />
      <path d="M35 36 L41 42 L53 30" fill="none" stroke="#FFF" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
    </g>;

  if (compact) return <span className="logo-container" role="img" aria-label="transferscheck.com - Tus notificaciones en todas partes.">
    <svg className="logo-symbol" viewBox="0 0 84 84" aria-hidden="true">{symbol}</svg>
    <span className="logo-text-group" aria-hidden="true">
      <span className="logo-name">transfers<span>check</span><span className="logo-dot">.</span></span>
      <span className="slogan">Tus notificaciones en todas partes.</span>
    </span>
  </span>;

  return <svg className="brand-logo" viewBox="0 0 380 84" role="img" aria-label="transferscheck.com - Tus notificaciones en todas partes.">
    {symbol}
    <g transform="translate(86, 46)" fontFamily="system-ui, -apple-system, Segoe UI, sans-serif">
      <text fontSize="32" fontWeight="800" letterSpacing="-0.6" fill="#0F172A">transfers<tspan fill="#3483FA">check</tspan><tspan fill="#00A650">.</tspan></text>
      <text x="2" y="25" fontSize="13" fontWeight="600" fill="#475569">Tus notificaciones en todas partes.</text>
    </g>
  </svg>;
}
