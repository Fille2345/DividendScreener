# DividendScreener

Intrinsic-value analysis for European dividend stocks.

- `src/DividendScreener.Core`: domain models, the valuation engine, and the `IMarketDataClient` abstraction (no web dependencies)
- `src/DividendScreener.Infrastructure`: market data providers (FMP, Eulerpool), routing and caching
- `src/DividendScreener.Api`: ASP.NET Core minimal API (.NET 10)
- `tests/DividendScreener.Core.Tests`: xUnit tests for the valuation math
- `tests/DividendScreener.Infrastructure.Tests`: provider parsing (mock HTTP), routing and caching tests
- `tests/DividendScreener.Api.Tests`: endpoint tests
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

| Provider | Use |
|---|---|
| `Router` (default) | Tickers with an exchange suffix (`SAN.PA`, `NDA-SE.ST`) go to Eulerpool; plain tickers (`KO`) go to FMP. If the first provider fails or doesn't know the ticker, the other one is tried. |
| `Fmp` | [Financial Modeling Prep](https://site.financialmodelingprep.com/) `/stable` API only. Free plan: US listings, 250 requests/day. |
| `Eulerpool` | [Eulerpool](https://eulerpool.com/developers) `/api/1` only. Covers European listings. |
| `Sample` | Offline, one fictitious company (`STDX`). |

Router outcomes: if neither provider knows the ticker → 404. If the first provider says "unknown ticker" and
the fallback errors → 404 (the first provider's answer wins). If both fail → 502 naming both reasons.

Snapshots are cached per ticker for `MarketData:CacheDuration` (default 15 minutes). One lookup costs
3 FMP requests or 5 Eulerpool requests, plus the other provider's requests when it falls back.

### API keys

Keys are read from `FMP_API_KEY` and `EULERPOOL_API_KEY` and are never stored in the repo:

```sh
dotnet user-secrets set FMP_API_KEY "<your key>" --project src/DividendScreener.Api        # Development only
dotnet user-secrets set EULERPOOL_API_KEY "<your key>" --project src/DividendScreener.Api
export FMP_API_KEY="<your key>" EULERPOOL_API_KEY="<your key>"                           # any environment
```

FMP takes the key as a query parameter (redacted in logs); Eulerpool takes it as a Bearer header.

### How provider data is interpreted

- **Dividend history** is summed per calendar year of the ex-dividend date (split-adjusted on FMP).
  The current year is excluded because it is incomplete. Years with no payment inside the 11-year window
  count as 0, so a suspended or cancelled dividend shows up as a cut.
- **FCF payout** uses the latest complete annual cash-flow statement. Eulerpool reports statements in millions
  and publishes the newest year early with capex and dividends still at 0; such rows are skipped. If no
  statement is available, the valuation still runs and the payout ratio is reported as unavailable.
- **Eulerpool identifiers**: the profile endpoint resolves the ticker (`SAN.PA` → `FR0000120578`, while plain
  `SAN` is Santander's US ADR); every other call uses that ISIN. A bare ISIN works too.
- **Errors**: missing or rejected keys, rate limits, data outside your plan, timeouts and malformed responses
  → 502 with an explanation. Network errors and 5xx responses are retried twice; 429 is not.

## Debugging market data

- **Error details**: a failed lookup's 404/502 `detail` names each provider's outcome, including the HTTP status,
  the request path, the provider's own error text, or the JSON parsing error and field path, for example
  `Eulerpool does not recognise 'X.ST' (HTTP 404 from equity/profile/X.ST: Security not found). Fallback: ...`.
  Unknown tickers are cached with their reason for 5 minutes.
- **Logs**: every provider request is logged with its exact URL (never the API key), status and timing.
  Failures are logged as warnings with the first 500 characters of the response body.
- **Raw Eulerpool responses** (Development only, bypasses caching and valuation):

  ```
  GET /api/debug/eulerpool/{identifier}?endpoint=profile|quotes|price-metrics|cashflowstatement|dividends
  ```

  Returns the upstream URL, status, timing and body as-is. `{identifier}` can be a ticker (`NDA-SE.ST`,
  `VOLV%20B.ST`) or an ISIN. Each call spends one request of your Eulerpool quota.

## Run

```sh
dotnet test                                                  # backend tests
dotnet run --project src/DividendScreener.Api --launch-profile http   # API on http://localhost:5020, OpenAPI at /openapi/v1.json
cd client && npm install && npm run dev                      # UI on http://localhost:5173, proxies /api to the API
```
