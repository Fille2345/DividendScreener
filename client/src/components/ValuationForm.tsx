import { useState, type FormEvent } from 'react'
import type { MarketAssumptions, ValuationQuery } from '../types/valuation'

export interface ValuationFormValues {
  ticker: string
  query: ValuationQuery
}

interface Props {
  defaults: MarketAssumptions | null
  loading: boolean
  onSubmit: (values: ValuationFormValues) => void
}

/** Parses a percent text field ("2.7") into a decimal fraction (0.027); blank means "use the default". */
function percentToFraction(text: string): number | undefined {
  if (text.trim() === '') return undefined
  const n = Number(text.replace(',', '.'))
  return Number.isFinite(n) ? n / 100 : undefined
}

function parsePositive(text: string): number | undefined {
  if (text.trim() === '') return undefined
  const n = Number(text.replace(',', '.'))
  return Number.isFinite(n) && n > 0 ? n : undefined
}

export function ValuationForm({ defaults, loading, onSubmit }: Props) {
  const [ticker, setTicker] = useState('')
  const [costBasis, setCostBasis] = useState('')
  const [riskFree, setRiskFree] = useState('')
  const [marketReturn, setMarketReturn] = useState('')
  const [maxGrowth, setMaxGrowth] = useState('')

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault()
    const trimmed = ticker.trim().toUpperCase()
    if (!trimmed) return
    onSubmit({
      ticker: trimmed,
      query: {
        costBasis: parsePositive(costBasis),
        riskFreeRate: percentToFraction(riskFree),
        marketReturn: percentToFraction(marketReturn),
        maxGrowthRate: percentToFraction(maxGrowth),
      },
    })
  }

  const placeholderPct = (fraction: number | undefined) =>
    fraction === undefined ? '' : (fraction * 100).toFixed(2).replace(/\.?0+$/, '')

  return (
    <form className="card form" onSubmit={handleSubmit}>
      <div className="form-main">
        <label className="field field-ticker">
          <span>Ticker</span>
          <input
            value={ticker}
            onChange={(e) => setTicker(e.target.value)}
            placeholder="e.g. SAN.PA"
            autoComplete="off"
            spellCheck={false}
            autoFocus
          />
        </label>
        <label className="field">
          <span>Your purchase price (optional)</span>
          <input
            value={costBasis}
            onChange={(e) => setCostBasis(e.target.value)}
            inputMode="decimal"
            placeholder="for yield on cost"
          />
        </label>
        <button type="submit" disabled={loading || ticker.trim() === ''}>
          {loading ? 'Valuing…' : 'Value stock'}
        </button>
      </div>

      <details className="assumptions">
        <summary>Assumptions</summary>
        <div className="form-grid">
          <label className="field">
            <span>Risk-free rate (%)</span>
            <input
              value={riskFree}
              onChange={(e) => setRiskFree(e.target.value)}
              inputMode="decimal"
              placeholder={placeholderPct(defaults?.riskFreeRate)}
            />
          </label>
          <label className="field">
            <span>Expected market return (%)</span>
            <input
              value={marketReturn}
              onChange={(e) => setMarketReturn(e.target.value)}
              inputMode="decimal"
              placeholder={placeholderPct(defaults?.expectedMarketReturn)}
            />
          </label>
          <label className="field">
            <span>Cap on perpetual growth g (%)</span>
            <input
              value={maxGrowth}
              onChange={(e) => setMaxGrowth(e.target.value)}
              inputMode="decimal"
              placeholder="no cap"
            />
          </label>
        </div>
        <p className="hint">Leave blank to use the server defaults. For euro-area stocks, Rf usually tracks the 10-year Bund yield.</p>
      </details>
    </form>
  )
}
