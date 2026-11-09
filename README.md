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

Providers implement `IMarketDataClient` and are selected with `MarketData:Provider` in `appsettings.json`:

- `Fmp` (default): [Financial Modeling Prep](https://site.financialmodelingprep.com/) `/stable` API.
  One lookup makes three requests (`profile`, `cash-flow-statement`, `dividends`).
- `Sample`: offline, one fictitious company (`STDX`), labelled "Sample data (fictitious)".

Snapshots are cached per ticker for `MarketData:CacheDuration` (default 15 minutes), which keeps
repeated valuations within FMP's free quota (250 requests a day).

### FMP API key

The key is read from the `FMP_API_KEY` configuration value and is never stored in the repo. Either:

```sh
dotnet user-secrets set FMP_API_KEY "<your key>" --project src/DividendScreener.Api   # Development only
export FMP_API_KEY="<your key>"                                                      # any environment
```

### How FMP data is interpreted

- **Dividend history** is summed per calendar year of the ex-dividend date, using split-adjusted amounts.
  The current year is excluded because it is incomplete. Years with no payment inside the 11-year window
  count as 0, so a suspended or cancelled dividend shows up as a cut.
- **FCF payout** uses the latest annual cash-flow statement. If the statement is missing or not covered by
  your FMP plan, the valuation still runs and the payout ratio is reported as unavailable.
- **Errors**: unknown ticker → 404. Missing key, rejected key, rate limit, data not covered by your plan,
  timeouts and malformed responses → 502 with an explanation. Network errors and 5xx responses are retried
  twice; 429 is not, since FMP's limits are daily.

## Run

```sh
dotnet test                                                  # backend tests
dotnet run --project src/DividendScreener.Api --launch-profile http   # API on http://localhost:5020, OpenAPI at /openapi/v1.json
cd client && npm install && npm run dev                      # UI on http://localhost:5173, proxies /api to the API
```
