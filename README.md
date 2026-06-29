# TradingAgent

TradingAgent is a .NET 8 webhook-driven trading signal assistant for TradingView, Claude, Yahoo Finance, Telegram, SQLite, Docker, and Nginx.

## Features

- Receives TradingView webhooks at `POST /api/tradingview/webhook`
- Enforces a shared `WEBHOOK_SECRET` from the request body or headers
- Fetches live Yahoo Finance market data (EMA, RSI, MACD, ATR, volume, 52-week range)
- Uses Claude as a strict smart filter (not just a formatter)
- Sends filtered signal summaries to Telegram
- Tracks paper positions automatically with P/L
- Audits every webhook request with source detection and safe test mode
- Checks market status (NASDAQ/NYSE) before processing signals — ignores when closed
- Includes a one-page dashboard at `/`
- Cleans up records older than `RETENTION_DAYS`

## Sprint changelog (2026-06-25)

### Claude & Telegram integration fix

- Fixed Anthropic Messages API integration (`POST https://api.anthropic.com/v1/messages`)
- Supports `CLAUDE_API_KEY` and `ANTHROPIC_API_KEY` (CLAUDE wins if both set)
- Default model: `claude-haiku-4-5-20251001`
- Resilient webhook flow: always saves signal first, never fails on Claude errors
- Improved Telegram formatting and structured logging
- Diagnostic endpoints: `GET /api/test-claude`, `GET /api/test-telegram`
- Local `.env` loading via `DotEnvLoader`

### Sprint 1 — Yahoo Finance market context

- `YahooFinanceService` fetches 1-year daily chart data per symbol
- Calculates: current price, EMA 9/20/50, RSI 14, MACD, ATR, volume, avg volume (20), 52-week high/low
- Stores market data in `SignalMarketData` (SQLite)
- Passes `MarketContext` DTO to Claude in every analysis prompt
- Diagnostic endpoint: `GET /api/test-yahoo`

### Sprint 2 — Smart AI filter

- Claude returns strict JSON filter decisions: `decision`, `confidence`, `risk`, `reason`, `stopLoss`, `takeProfit`, `riskRewardRatio`, `positionSizePercent`, `shouldNotify`
- Telegram only sends when `shouldNotify=true` AND `confidence >= MIN_CONFIDENCE_TO_NOTIFY` (or `SEND_IGNORED_SIGNALS=true`)
- Low-confidence WAIT/IGNORE signals are stored and shown on dashboard but not sent to Telegram
- Settings endpoint: `GET /api/settings`
- Dashboard shows Claude decision, confidence, risk, RSI, volume spike, notified status

### Sprint 3 — Paper position manager

- `Position` entity tracks OPEN/CLOSED paper trades
- Opens position on Claude BUY when notification rules pass (one per symbol)
- Closes position on Claude SELL or EXIT
- Calculates profit/loss and profit/loss %
- Telegram messages for position open (`📥`) and close (`📤`)
- Dashboard: open positions, closed positions, total P/L, win rate, average win/loss
- Position APIs: `GET /api/positions`, `/open`, `/closed`, `POST /api/positions/{id}/close`, `PATCH /api/positions/{id}`

### Sprint 4 — Signal source audit & safe test mode

- `WebhookRequestLog` audits every request (IP, User-Agent, headers, payload, result)
- Source detection: `TRADINGVIEW`, `CURSOR_TEST`, `POSTMAN_TEST`, `UNKNOWN`
- Test mode via `X-Test-Mode: true` header or `source: CURSOR_TEST` / `POSTMAN_TEST` in payload
- Test signals: stored and shown with TEST badge, no paper trades unless `ALLOW_TEST_TRADES=true`
- Test Telegram only when `SEND_TEST_TELEGRAM=true` (prefixed with `🧪 TEST`)
- Safe test endpoint: `POST /api/test-webhook`
- Webhook history: `GET /api/webhooks/history`, `GET /api/webhooks/history/{id}`, `DELETE /api/webhooks/history`
- Dashboard webhook history section with Real / Test / All filter

### Sprint 5 — Market status provider architecture

- Provider-based market awareness: `IMarketProvider`, `MarketStatusService`, `MarketCalendarService`
- Fully implemented: **NASDAQ** and **NYSE** (Mon–Fri 09:30–16:00 `America/New_York`)
- Placeholders: **CRYPTO** and **FOREX** (safe `CLOSED` response, easy to extend later)
- Detects: `IsOpen`, `IsWeekend`, `IsHoliday`, `IsPreMarket`, `IsAfterHours`, `NextOpenTimeUtc`, `NextCloseTimeUtc`
- Status values: `OPEN`, `CLOSED`, `PRE_MARKET`, `AFTER_HOURS`, `HOLIDAY`, `WEEKEND`
- US market holidays with observed dates when they fall on a weekend
- Webhook gate runs **after** saving the signal, **before** Yahoo / Claude / Telegram / positions
- Ignored signals: `ClaudeAction=IGNORE`, `ShortReason=Market closed`, `IgnoredReason`, `IgnoredBy=MarketStatus`
- Optional Telegram when market closed: `SEND_MARKET_CLOSED_NOTIFICATIONS=true`
- Dashboard: Market Status card + signal columns for market status and ignored reason
- Market APIs: `GET /api/market/status`, `/api/market/status/{market}`, `/api/market/calendar`, `/api/test-market`
- Unit tests: `src/TradingAgent.Tests` (open, weekend, holiday, pre-market, after-hours, observed holiday, DST)

## Environment variables

Copy `.env.example` to `.env` and fill in the values:

```env
TELEGRAM_BOT_TOKEN=
TELEGRAM_CHAT_ID=
CLAUDE_API_KEY=
CLAUDE_MODEL=claude-haiku-4-5-20251001
CLAUDE_ENABLED=true
MIN_CONFIDENCE_TO_NOTIFY=70
SEND_IGNORED_SIGNALS=false
PAPER_TRADING_ENABLED=true
DEFAULT_POSITION_QUANTITY=1
ALLOW_TEST_TRADES=false
SEND_TEST_TELEGRAM=false
MARKET_PROVIDER=NASDAQ
MARKET_TIMEZONE=America/New_York
ALLOW_PREMARKET=false
ALLOW_AFTER_HOURS=false
IGNORE_SIGNALS_WHEN_MARKET_CLOSED=true
SEND_MARKET_CLOSED_NOTIFICATIONS=false
DATABASE_PATH=/app/data/tradingagent.db
RETENTION_DAYS=30
WEBHOOK_SECRET=
ASPNETCORE_URLS=http://+:8080
```

> `WEBHOOK_SECRET` is required. The app will fail to start without it.

| Variable | Default | Description |
|----------|---------|-------------|
| `CLAUDE_API_KEY` | — | Anthropic API key (or use `ANTHROPIC_API_KEY`) |
| `CLAUDE_MODEL` | `claude-haiku-4-5-20251001` | Claude model name |
| `CLAUDE_ENABLED` | `true` | Enable/disable Claude analysis |
| `MIN_CONFIDENCE_TO_NOTIFY` | `70` | Minimum confidence for Telegram notification |
| `SEND_IGNORED_SIGNALS` | `false` | Send Telegram for all signals including low-confidence |
| `PAPER_TRADING_ENABLED` | `true` | Enable paper position tracking |
| `DEFAULT_POSITION_QUANTITY` | `1` | Default paper trade quantity |
| `ALLOW_TEST_TRADES` | `false` | Allow test signals to open/close paper positions |
| `SEND_TEST_TELEGRAM` | `false` | Send Telegram for test signals (prefixed `🧪 TEST`) |
| `MARKET_PROVIDER` | `NASDAQ` | Market for status checks: `NASDAQ`, `NYSE`, `CRYPTO`, `FOREX` |
| `MARKET_TIMEZONE` | `America/New_York` | Reference timezone for market hours |
| `ALLOW_PREMARKET` | `false` | Process signals during pre-market (04:00–09:30 ET) |
| `ALLOW_AFTER_HOURS` | `false` | Process signals during after-hours (16:00–20:00 ET) |
| `IGNORE_SIGNALS_WHEN_MARKET_CLOSED` | `true` | Skip Yahoo/Claude/Telegram/positions when market is not open |
| `SEND_MARKET_CLOSED_NOTIFICATIONS` | `false` | Telegram alert when a signal is ignored due to market status |

## Local run

```bash
cd src/TradingAgent
dotnet restore
dotnet run
```

Dashboard: `http://localhost:5044/` (or port from `launchSettings.json` / `ASPNETCORE_URLS`)

### Unit tests

```bash
cd src/TradingAgent.Tests
dotnet test
```

## Sample TradingView webhook JSON

See `samples/tradingview-webhook.json`.

```json
{
  "symbol": "NVDA",
  "signal": "BUY",
  "price": "185.30",
  "timeframe": "15m",
  "strategy": "EMA_RSI",
  "rsi": "58",
  "volume": "high",
  "source": "TRADINGVIEW",
  "timestamp": "{{timenow}}",
  "secret": "replace-with-your-webhook-secret"
}
```

## curl test commands

### Real TradingView webhook

```bash
curl -X POST http://localhost:8080/api/tradingview/webhook \
  -H "Content-Type: application/json" \
  -H "User-Agent: TradingView/1.0" \
  -d '{"symbol":"NVDA","signal":"BUY","price":"185.30","timeframe":"15m","strategy":"EMA_RSI","source":"TRADINGVIEW","secret":"your-secret"}'
```

### Safe test webhook (Cursor)

```bash
curl -X POST http://localhost:8080/api/test-webhook \
  -H "Content-Type: application/json" \
  -d '{"symbol":"NVDA","signal":"BUY","price":"195.00"}'
```

### Postman test signal

```bash
curl -X POST http://localhost:8080/api/tradingview/webhook \
  -H "Content-Type: application/json" \
  -H "X-Test-Mode: true" \
  -d '{"symbol":"NVDA","signal":"BUY","price":"195.00","source":"POSTMAN_TEST","secret":"your-secret"}'
```

### Diagnostics

```bash
curl http://localhost:8080/api/test-claude
curl http://localhost:8080/api/test-telegram
curl http://localhost:8080/api/test-yahoo
curl http://localhost:8080/api/settings
curl http://localhost:8080/api/webhooks/history
curl http://localhost:8080/api/market/status
curl http://localhost:8080/api/market/status/NYSE
curl "http://localhost:8080/api/market/calendar?year=2026"
curl "http://localhost:8080/api/test-market?datetimeUtc=2026-01-01T15:00:00Z&market=NASDAQ"
```

### Market status simulation

Use `GET /api/test-market` to test holidays, weekends, pre-market, and after-hours without changing the system clock:

```bash
# Holiday (New Year's Day)
curl "http://localhost:8080/api/test-market?datetimeUtc=2026-01-01T15:00:00Z&market=NASDAQ"

# Regular market open (Tuesday midday ET)
curl "http://localhost:8080/api/test-market?datetimeUtc=2026-06-23T16:00:00Z&market=NASDAQ"

# Pre-market
curl "http://localhost:8080/api/test-market?datetimeUtc=2026-06-23T12:00:00Z&market=NASDAQ"

# After-hours
curl "http://localhost:8080/api/test-market?datetimeUtc=2026-06-23T21:00:00Z&market=NASDAQ"
```

> Webhooks always use live UTC time. During closed hours, signals are ignored when `IGNORE_SIGNALS_WHEN_MARKET_CLOSED=true` (default).

## Telegram chat ID

1. Start a chat with your bot.
2. Send any message to the bot.
3. Open `https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getUpdates`
4. Find `message.chat.id` in the JSON response and use it as `TELEGRAM_CHAT_ID`.

## Claude API key

1. Create an Anthropic account.
2. Generate an API key from the Anthropic console.
3. Set `CLAUDE_API_KEY` (or `ANTHROPIC_API_KEY`).
4. Set `CLAUDE_ENABLED=false` to process webhooks without Claude analysis.

## Docker Compose (development)

From the repository root with Docker Desktop running:

```bash
docker compose --env-file .env up --build
```

- Nginx listens on `http://localhost:8085`
- The .NET app listens internally on port `8080`
- SQLite data persists in the `tradingagent-data` volume

---

## Deployment package (Windows)

Deploy TradingAgent on any Windows machine with **only Docker Desktop** installed. No Visual Studio, no .NET SDK, and no manual `docker` commands on the target machine.

### Build the package (developer machine)

Requirements on the **build machine**: .NET 8 SDK, Docker Desktop (running).

```powershell
.\build-deployment-package.ps1
```

Use `-NoPrompt` to skip the “open folder” question:

```powershell
.\build-deployment-package.ps1 -NoPrompt
```

Legacy alias: `.\scripts\Create-DeploymentPackage.ps1`

Each run **completely replaces** the previous package:

1. Stops local Docker Compose (if running)
2. Deletes old `deploy-package/`, ZIP archives, `publish/`, `artifacts/`
3. Builds .NET (Release) and publishes
4. Builds Docker image `tradingagent:latest`
5. Exports `tradingagent-image.tar`
6. Generates `.env`, scripts, README, `docker-compose.yml`, `nginx.conf`
7. Creates timestamped ZIP

**Output:**

| Artifact | Description |
|----------|-------------|
| `deploy-package/` | Complete deployment folder |
| `TradingAgent_Deployment_yyyyMMdd_HHmm.zip` | Portable ZIP for another PC |

### Package contents

| File | Purpose |
|------|---------|
| `tradingagent-image.tar` | Pre-built Docker image |
| `docker-compose.yml` | App + Nginx stack (bind-mount `./data`) |
| `nginx.conf` | Reverse proxy (port 8085 → app) |
| `.env` | Ready-to-run config (API keys empty) |
| `.env.example` | Template with empty secrets |
| `README.md` | Target-machine deployment guide |
| `start.ps1` | Start stack (Docker checks, load image, health wait) |
| `stop.ps1` | Stop stack |
| `restart.ps1` | Restart stack |
| `status.ps1` | Container state, uptime, ports, disk |
| `logs.ps1` | `docker compose logs -f` |
| `backup-db.ps1` | Backup SQLite to `backups/` |
| `restore-db.ps1` | Interactive database restore |
| `update-image.ps1` | Load new image tar and restart |
| `clean.ps1` | Prune unused Docker resources (keeps DB/backups) |
| `healthcheck.ps1` | PASS/FAIL system health report |

No source code is included in the package.

### `.env` in the package

The build script generates a ready-to-run `.env` from your development configuration. Non-secret values are pre-filled (e.g. `WEBHOOK_SECRET`, market settings, confidence thresholds). **Only API keys are left empty** for security:

- `CLAUDE_API_KEY` / `ANTHROPIC_API_KEY`
- `TELEGRAM_BOT_TOKEN`
- `TELEGRAM_CHAT_ID`

Edit these on the target machine before going live.

### Install on target machine

1. Install [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/)
2. Extract `TradingAgent_Deployment_*.zip` to a folder (e.g. `C:\TradingAgent`)
3. Open PowerShell in that folder
4. Edit `.env` and add your API keys
5. Run:

```powershell
.\start.ps1
```

`start.ps1` will:

- Verify Docker Desktop and Docker Engine
- Load `tradingagent-image.tar` if the image is missing
- Create `data/`, `logs/`, `backups/` folders
- Start containers with `docker compose up -d`
- Wait until the API responds
- Display URLs and container status

### URLs (deployed stack)

| Service | URL |
|---------|-----|
| Dashboard | http://localhost:8085/ |
| API settings | http://localhost:8085/api/settings |
| Market status | http://localhost:8085/api/market/status |
| TradingView webhook | http://localhost:8085/api/tradingview/webhook |
| Test webhook | http://localhost:8085/api/test-webhook |

> OpenAPI/Swagger is not included. Use the dashboard and `/api/*` diagnostic endpoints.

### Operations scripts

```powershell
.\start.ps1          # Start (first time or after stop)
.\stop.ps1           # Stop containers
.\restart.ps1        # Restart containers
.\status.ps1         # Runtime status
.\logs.ps1           # Follow logs
.\backup-db.ps1      # Backup database
.\restore-db.ps1     # Restore from backup
.\update-image.ps1   # Load new tradingagent-image.tar
.\clean.ps1          # Prune unused Docker resources
.\healthcheck.ps1    # Full health check (PASS/FAIL)
```

### Data locations (deployment)

| Path | Description |
|------|-------------|
| `.\data\tradingagent.db` | SQLite database |
| `.\backups\` | Database backups (`tradingagent_yyyyMMdd_HHmm.db`) |
| `.\logs\` | Reserved for future log exports |

### Deployment troubleshooting

| Issue | Fix |
|-------|-----|
| Build fails at Docker step | Start Docker Desktop and re-run `.\build-deployment-package.ps1` |
| `Docker engine not running` | Start Docker Desktop, wait until ready, run `.\start.ps1` |
| Port 8085 in use | Change `8085:80` in `docker-compose.yml` |
| Webhook 400 Invalid JSON | Send `price` and `rsi` as **strings** in JSON |
| Webhook 401 | Check `WEBHOOK_SECRET` in `.env` matches payload |
| Health check FAIL | Run `.\logs.ps1` and verify API keys in `.env` |

---

### Webhooks & signals

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/tradingview/webhook` | Receive TradingView alert |
| POST | `/api/test-webhook` | Safe test signal (always `IsTest=true`) |
| GET | `/api/signals` | List signals |
| GET | `/api/signals/{id}` | Get signal by ID |
| PATCH | `/api/signals/{id}` | Update signal notes/P/L |
| DELETE | `/api/signals/{id}` | Delete signal |

### Webhook audit

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/webhooks/history` | List webhook logs (`?filter=real\|test\|all`) |
| GET | `/api/webhooks/history/{id}` | Get webhook log by ID |
| DELETE | `/api/webhooks/history` | Clear all webhook logs |

### Positions

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/positions` | All paper positions |
| GET | `/api/positions/open` | Open positions |
| GET | `/api/positions/closed` | Closed positions |
| POST | `/api/positions/{id}/close` | Manually close position |
| PATCH | `/api/positions/{id}` | Update position notes |

### Market status

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/market/status` | Current status for configured market (`MARKET_PROVIDER`) |
| GET | `/api/market/status/{market}` | Status for `NASDAQ`, `NYSE`, `CRYPTO`, or `FOREX` |
| GET | `/api/market/calendar` | US holidays for configured market (`?year=2026`) |
| GET | `/api/test-market` | Test status (`?datetimeUtc=...&market=NASDAQ`) |

### Diagnostics & settings

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/test-claude` | Test Claude API connection |
| GET | `/api/test-telegram` | Send Telegram test message |
| GET | `/api/test-yahoo` | Fetch NVDA market data |
| GET | `/api/settings` | View current configuration |

## Webhook processing flow

```
TradingView / Test request
        ↓
Log WebhookRequestLog (audit)
        ↓
Detect source (TRADINGVIEW / TEST / UNKNOWN)
        ↓
Save signal to SQLite
        ↓
Check market status (NASDAQ/NYSE hours + US holidays)
        ↓
Market closed / weekend / holiday / pre-market / after-hours?
  → Mark signal ignored, optional market-closed Telegram, return HTTP 200
        ↓ (market open, or ALLOW_PREMARKET / ALLOW_AFTER_HOURS)
Fetch Yahoo market data → Store SignalMarketData
        ↓
Claude smart filter analysis
        ↓
Update signal with decision
        ↓
Telegram? (filtered; test mode rules apply)
        ↓
Paper position? (skipped for test unless ALLOW_TEST_TRADES)
        ↓
Return safe JSON response (always HTTP 200 on success)
```

## Notes

- SQLite schema is created and migrated automatically on startup.
- The dashboard is intentionally simple so authentication can be added later.
- Secrets are not hardcoded and are not written to logs.
- Test signals are clearly marked and isolated from real trading by default.
- Crypto and Forex market providers are placeholders; set `MARKET_PROVIDER` to `NASDAQ` or `NYSE` for US equities.
