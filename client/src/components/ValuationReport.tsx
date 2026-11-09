import type { StockValuationResponse } from '../types/valuation'
import { formatCompactMoney, formatMoney, formatNumber, formatPercent } from '../lib/format'

interface MetricProps {
  label: string
  value: string
  note?: string
}

function Metric({ label, value, note }: MetricProps) {
  return (
    <div className="metric">
      <span className="metric-label">{label}</span>
      <span className="metric-value">{value}</span>
      {note && <span className="metric-note">{note}</span>}
    </div>
  )
}

export function ValuationReport({ data }: { data: StockValuationResponse }) {
  const { stock, metrics, market, valuation: v, source, retrievedAt } = data
  const ccy = v.currency
  const isSample = source.toLowerCase().includes('sample')

  const mos = v.marginOfSafety
  const verdict =
    mos === null ? null : mos >= 0 ? 'Trades below intrinsic value' : 'Trades above intrinsic value'

  const history = [...metrics.dividendHistory].sort((a, b) => b.fiscalYear - a.fiscalYear)

  return (
    <section className="report">
      <header className="report-header">
        <div>
          <h2>{v.name}</h2>
          <p className="muted">
            {v.ticker} · {stock.exchange} · {ccy}
          </p>
        </div>
        <span className={`badge ${isSample ? 'badge-warn' : ''}`}>
          {source} · {new Date(retrievedAt).toLocaleString()}
        </span>
      </header>

      <div className="hero card">
        <div className="hero-figure">
          <span className="metric-label">Intrinsic value (Gordon Growth)</span>
          <span className="hero-value">{formatMoney(v.intrinsicValue, ccy)}</span>
        </div>
        <div className="hero-figure">
          <span className="metric-label">Current price</span>
          <span className="hero-value">{formatMoney(v.currentPrice, ccy)}</span>
        </div>
        <div className="hero-figure">
          <span className="metric-label">Margin of safety</span>
          <span className={`hero-value ${mos === null ? '' : mos >= 0 ? 'positive' : 'negative'}`}>
            {formatPercent(mos, 1)}
          </span>
          {verdict && <span className="metric-note">{verdict}</span>}
        </div>
      </div>

      {v.warnings.length > 0 && (
        <ul className="warnings">
          {v.warnings.map((w) => (
            <li key={w}>{w}</li>
          ))}
        </ul>
      )}

      <div className="grid">
        <div className="card">
          <h3>Discount rate (CAPM)</h3>
          <div className="metrics">
            <Metric label="Beta (β)" value={formatNumber(v.beta)} />
            <Metric label="Risk-free rate" value={formatPercent(market.riskFreeRate)} />
            <Metric label="Market return" value={formatPercent(market.expectedMarketReturn)} />
            <Metric label="Required return (r)" value={formatPercent(v.requiredReturn)} note="Rf + β × (Rm − Rf)" />
          </div>
        </div>

        <div className="card">
          <h3>Dividend growth</h3>
          <div className="metrics">
            <Metric label="3-year CAGR" value={formatPercent(v.dividendCagr3Y)} />
            <Metric label="5-year CAGR" value={formatPercent(v.dividendCagr5Y)} />
            <Metric label="10-year CAGR" value={formatPercent(v.dividendCagr10Y)} />
            <Metric
              label="Growth used (g)"
              value={formatPercent(v.growthRate)}
              note={v.growthRatePeriodYears ? `from ${v.growthRatePeriodYears}-year CAGR` : undefined}
            />
          </div>
        </div>

        <div className="card">
          <h3>Yield &amp; payout</h3>
          <div className="metrics">
            <Metric label="Dividend (D₀)" value={formatMoney(v.currentDividend, ccy)} />
            <Metric label="Next dividend (D₁)" value={formatMoney(v.nextDividend, ccy)} />
            <Metric label="Dividend yield" value={formatPercent(v.dividendYield)} />
            <Metric
              label="Yield on cost"
              value={formatPercent(v.yieldOnCost)}
              note={v.yieldOnCost === null ? 'enter a purchase price' : undefined}
            />
            <Metric
              label="FCF payout ratio"
              value={formatPercent(v.fcfPayoutRatio, 1)}
              note={`${formatCompactMoney(Math.abs(metrics.dividendsPaid), ccy)} / ${formatCompactMoney(metrics.freeCashFlow, ccy)}`}
            />
          </div>
        </div>

        <div className="card">
          <h3>Dividend history</h3>
          <table className="history">
            <thead>
              <tr>
                <th scope="col">Year</th>
                <th scope="col">Dividend / share</th>
              </tr>
            </thead>
            <tbody>
              {history.map((d) => (
                <tr key={d.fiscalYear}>
                  <td>{d.fiscalYear}</td>
                  <td>{formatMoney(d.dividendPerShare, ccy)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </section>
  )
}
