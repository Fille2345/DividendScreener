import { useEffect, useState } from 'react'
import { fetchHealth } from './api/client'

type ApiStatus = 'checking' | 'online' | 'offline'

function App() {
  const [status, setStatus] = useState<ApiStatus>('checking')

  useEffect(() => {
    fetchHealth().then((ok) => setStatus(ok ? 'online' : 'offline'))
  }, [])

  return (
    <main className="shell">
      <h1>DividendScreener</h1>
      <p>Intrinsic value of European dividend stocks via CAPM and the Gordon Growth Model.</p>
      <p className={`status status-${status}`}>Valuation API: {status}</p>
    </main>
  )
}

export default App
