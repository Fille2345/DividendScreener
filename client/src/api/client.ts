import type {
  MarketAssumptions,
  StockValuationResponse,
  ValuationQuery,
  ValuationRequest,
  ValuationResult,
} from '../types/valuation'

/** Error carrying the API's ProblemDetails title/detail, so the UI can show a useful message. */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function readJson<T>(res: Response): Promise<T> {
  if (res.ok) return (await res.json()) as T

  let message = `Request failed (${res.status})`
  try {
    const problem = (await res.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> }
    const fieldErrors = problem.errors ? Object.values(problem.errors).flat().join(' ') : ''
    message = problem.detail ?? (fieldErrors || problem.title || message)
  } catch {
    // Body was not JSON; keep the generic message.
  }
  throw new ApiError(res.status, message)
}

export async function fetchHealth(): Promise<boolean> {
  try {
    const res = await fetch('/api/health')
    return res.ok
  } catch {
    return false
  }
}

export async function fetchMarketAssumptions(): Promise<MarketAssumptions> {
  return readJson<MarketAssumptions>(await fetch('/api/market-assumptions'))
}

export async function fetchValuation(
  ticker: string,
  query: ValuationQuery,
  signal?: AbortSignal,
): Promise<StockValuationResponse> {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && Number.isFinite(value)) params.set(key, String(value))
  }
  const qs = params.size > 0 ? `?${params}` : ''
  return readJson<StockValuationResponse>(
    await fetch(`/api/valuations/${encodeURIComponent(ticker)}${qs}`, { signal }),
  )
}

export async function valueStock(request: ValuationRequest): Promise<ValuationResult> {
  return readJson<ValuationResult>(
    await fetch('/api/valuations', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    }),
  )
}
