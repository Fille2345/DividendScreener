// Mirrors DividendScreener.Core.Models. Rates are decimal fractions (0.05 = 5%).

export interface Stock {
  ticker: string
  name: string
  isin: string
  exchange: string
  currency: string
  currentPrice: number
  sector?: string | null
}

export interface AnnualDividend {
  fiscalYear: number
  dividendPerShare: number
}

export interface FinancialMetrics {
  beta: number
  freeCashFlow: number
  dividendsPaid: number
  dividendHistory: AnnualDividend[]
}

export interface MarketAssumptions {
  riskFreeRate: number
  expectedMarketReturn: number
}

export interface ValuationOptions {
  growthRatePeriodPreference?: number[]
  maxPerpetualGrowthRate?: number | null
}

export interface ValuationRequest {
  stock: Stock
  metrics: FinancialMetrics
  market: MarketAssumptions
  options?: ValuationOptions
}

export interface ValuationResult {
  ticker: string
  currency: string
  currentPrice: number
  requiredReturn: number
  dividendCagr3Y: number | null
  dividendCagr5Y: number | null
  dividendCagr10Y: number | null
  growthRate: number | null
  growthRatePeriodYears: number | null
  currentDividend: number
  nextDividend: number | null
  intrinsicValue: number | null
  marginOfSafety: number | null
  fcfPayoutRatio: number | null
  warnings: string[]
}
