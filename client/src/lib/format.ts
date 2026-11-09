const DASH = '—'

export function formatPercent(value: number | null | undefined, digits = 2): string {
  if (value === null || value === undefined) return DASH
  return new Intl.NumberFormat(undefined, {
    style: 'percent',
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(value)
}

export function formatMoney(value: number | null | undefined, currency: string): string {
  if (value === null || value === undefined) return DASH
  return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(value)
}

export function formatNumber(value: number | null | undefined, digits = 2): string {
  if (value === null || value === undefined) return DASH
  return new Intl.NumberFormat(undefined, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(value)
}

/** Large reporting-currency totals, e.g. 8 000 000 000 → "€8.0B". */
export function formatCompactMoney(value: number, currency: string): string {
  return new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency,
    notation: 'compact',
    maximumFractionDigits: 1,
  }).format(value)
}
