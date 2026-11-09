# DividendScreener

Intrinsic-value analysis for European dividend stocks.

- `src/DividendScreener.Core`: domain models and the valuation engine (no web dependencies)
- `src/DividendScreener.Api`: ASP.NET Core minimal API
- `tests/DividendScreener.Core.Tests`: xUnit tests for the valuation math
- `client`: React + TypeScript (Vite)

## Valuation model

| Step | Formula |
|---|---|
| Required return (CAPM) | r = Rf + β × (Rm − Rf) |
| Dividend growth (CAGR, 3/5/10y) | g = (D_end / D_start)^(1/n) − 1 |
| Intrinsic value (Gordon Growth) | V₀ = D₀ × (1 + g) / (r − g), requires r > g |
| FCF payout ratio | \|Dividends paid\| / Free cash flow |

g is taken from the first available CAGR window in `ValuationOptions.GrowthRatePeriodPreference`
(default 5y → 3y → 10y) and can be capped with `MaxPerpetualGrowthRate`.
All math runs in `decimal`; the CAGR n-th root is refined with Newton's method to full decimal precision.

## Run

```sh
dotnet test                                                  # backend tests
dotnet run --project src/DividendScreener.Api --launch-profile http   # API on http://localhost:5020, Swagger at /swagger
cd client && npm install && npm run dev                      # UI on http://localhost:5173, proxies /api to the API
```
