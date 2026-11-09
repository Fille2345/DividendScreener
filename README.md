# DividendScreener

Intrinsic-value analysis for European dividend stocks.

- `src/DividendScreener.Core`: domain models, the valuation engine, and the `IMarketDataClient` abstraction (no web dependencies)
- `src/DividendScreener.Infrastructure`: market data providers and caching
- `src/DividendScreener.Api`: ASP.NET Core minimal API (.NET 10)
- `tests/DividendScreener.Core.Tests`: xUnit tests for the valuation math
- `tests/DividendScreener.Api.Tests`: endpoint and caching tests
- `client`: React + TypeScript (Vite) dashboard

## Valuation model

| Step | Formula |
|---|---|
| Required return (CAPM) | r = Rf + β × (Rm − Rf) |
| Dividend growth (CAGR, 3/5/10y) | g = (D_end / D_start)^(1/n) − 1 |
| Intrinsic value (Gordon Growth) | V₀ = D₀ × (1 + g) / (r − g), requires r > g |
| FCF payout ratio | \|Dividends paid\| / Free cash flow |
| Dividend yield / yield on cost | D₀ / price, D₀ / purchase price |

g is taken from the first available CAGR window in `ValuationOptions.GrowthRatePeriodPreference`
(default 5y → 3y → 10y) and can be capped with `MaxPerpetualGrowthRate`.
All math runs in `decimal`; the CAGR n-th root is refined with Newton's method to full decimal precision.

## API

| Endpoint | Purpose |
|---|---|
| `GET /api/valuations/{ticker}` | Fetch market data and value the stock. Optional query: `costBasis`, `riskFreeRate`, `marketReturn`, `maxGrowthRate` (rates as fractions, e.g. `0.027`) |
| `POST /api/valuations` | Value caller-supplied inputs |
| `GET /api/market-assumptions` | Default Rf and Rm from `appsettings.json` |

## Market data

Providers implement `IMarketDataClient` and are selected with `MarketData:Provider` in `appsettings.json`.
Snapshots are cached per ticker for `MarketData:CacheDuration` (default 15 minutes).

Only the `Sample` provider exists so far. It serves one fictitious company, `STDX`, tagged
"Sample data (fictitious)" in every response. A real provider is still to be chosen.

## Run

```sh
dotnet test                                                  # backend tests
dotnet run --project src/DividendScreener.Api --launch-profile http   # API on http://localhost:5020, OpenAPI at /openapi/v1.json
cd client && npm install && npm run dev                      # UI on http://localhost:5173, proxies /api to the API
```
