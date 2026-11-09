import type { ValuationRequest, ValuationResult } from '../types/valuation'

export async function fetchHealth(): Promise<boolean> {
  try {
    const res = await fetch('/api/health')
    return res.ok
  } catch {
    return false
  }
}

export async function valueStock(request: ValuationRequest): Promise<ValuationResult> {
  const res = await fetch('/api/valuations', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
  if (!res.ok) throw new Error(`Valuation failed: ${res.status}`)
  return (await res.json()) as ValuationResult
}
