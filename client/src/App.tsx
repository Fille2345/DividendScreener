import { useEffect, useRef, useState } from 'react'
import { fetchMarketAssumptions, fetchValuation } from './api/client'
import { ValuationForm, type ValuationFormValues } from './components/ValuationForm'
import { ValuationReport } from './components/ValuationReport'
import type { MarketAssumptions, StockValuationResponse } from './types/valuation'

type State =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'done'; data: StockValuationResponse }

function App() {
  const [defaults, setDefaults] = useState<MarketAssumptions | null>(null)
  const [state, setState] = useState<State>({ kind: 'idle' })
  const inFlight = useRef<AbortController | null>(null)

  useEffect(() => {
    fetchMarketAssumptions()
      .then(setDefaults)
      .catch(() => setDefaults(null))
  }, [])

  const handleSubmit = async ({ ticker, query }: ValuationFormValues) => {
    inFlight.current?.abort()
    const controller = new AbortController()
    inFlight.current = controller

    setState({ kind: 'loading' })
    try {
      const data = await fetchValuation(ticker, query, controller.signal)
      setState({ kind: 'done', data })
    } catch (err) {
      if (controller.signal.aborted) return
      const message = err instanceof Error ? err.message : 'Something went wrong.'
      setState({ kind: 'error', message })
    }
  }

  return (
    <main className="shell">
      <header className="page-header">
        <h1>DividendScreener</h1>
        <p className="muted">
          Intrinsic value of dividend stocks with CAPM and the Gordon Growth Model.
        </p>
      </header>

      <ValuationForm defaults={defaults} loading={state.kind === 'loading'} onSubmit={handleSubmit} />

      {state.kind === 'error' && (
        <p className="error" role="alert">
          {state.message}
        </p>
      )}
      {state.kind === 'done' && <ValuationReport data={state.data} />}
      {state.kind === 'idle' && (
        <p className="muted empty">Enter a ticker to run a valuation. Try STDX for the built-in sample company.</p>
      )}
    </main>
  )
}

export default App
