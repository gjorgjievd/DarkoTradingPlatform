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

## Local run

```bash
cd src/TradingAgent
dotnet restore
dotnet run
```

Dashboard: `http://localhost:5044/` (or port from `launchSettings.json` / `ASPNETCORE_URLS`)

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
```

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

## Docker Compose

```bash
docker compose --env-file .env up --build
```

- Nginx listens on `http://localhost:8085`
- The .NET app listens internally on port `8080`
- SQLite data persists in the `tradingagent-data` volume

## API endpoints

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
