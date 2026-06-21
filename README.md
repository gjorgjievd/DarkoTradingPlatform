# TradingAgent

TradingAgent is a .NET 8 webhook-driven trading signal assistant for TradingView, Claude, Telegram, SQLite, Docker, and Nginx.

## Features

- Receives TradingView webhooks at `POST /api/tradingview/webhook`
- Enforces a shared `WEBHOOK_SECRET` from the request body or headers
- Optionally asks Claude for structured trade analysis
- Stores webhook payloads and analysis results in SQLite
- Sends final signal summaries to Telegram
- Includes a one-page dashboard at `/`
- Cleans up records older than `RETENTION_DAYS`

## Environment variables

Copy `.env.example` to `.env` and fill in the values:

```env
TELEGRAM_BOT_TOKEN=
TELEGRAM_CHAT_ID=
CLAUDE_API_KEY=
CLAUDE_MODEL=claude-3-5-sonnet-latest
CLAUDE_ENABLED=true
DATABASE_PATH=/app/data/tradingagent.db
RETENTION_DAYS=30
WEBHOOK_SECRET=
ASPNETCORE_URLS=http://+:8080
```

> `WEBHOOK_SECRET` is required. The app will fail to start without it.

## Local run

```bash
cd /home/runner/work/DarkoTradingPlatform/DarkoTradingPlatform/src/TradingAgent
dotnet restore
dotnet run
```

Dashboard: `http://localhost:8080/`

## Sample TradingView webhook JSON

See `/home/runner/work/DarkoTradingPlatform/DarkoTradingPlatform/samples/tradingview-webhook.json`.

```json
{
  "symbol": "NVDA",
  "signal": "BUY",
  "price": "185.30",
  "timeframe": "15m",
  "strategy": "EMA_RSI",
  "rsi": "58",
  "volume": "high",
  "timestamp": "{{timenow}}",
  "secret": "replace-with-your-webhook-secret"
}
```

## curl test command

```bash
curl -X POST http://localhost:8080/api/tradingview/webhook \
  -H "Content-Type: application/json" \
  -d @/home/runner/work/DarkoTradingPlatform/DarkoTradingPlatform/samples/tradingview-webhook.json
```

You can also send the secret in a header:

```bash
curl -X POST http://localhost:8080/api/tradingview/webhook \
  -H "Content-Type: application/json" \
  -H "X-Webhook-Secret: your-secret" \
  -d '{"symbol":"NVDA","signal":"BUY","price":"185.30","timeframe":"15m","strategy":"EMA_RSI","timestamp":"2026-06-21T10:00:00Z"}'
```

## Telegram chat ID

1. Start a chat with your bot.
2. Send any message to the bot.
3. Open `https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getUpdates`
4. Find `message.chat.id` in the JSON response and use it as `TELEGRAM_CHAT_ID`.

## Claude API key

1. Create an Anthropic account.
2. Generate an API key from the Anthropic console.
3. Set `CLAUDE_API_KEY` and optionally change `CLAUDE_MODEL`.
4. Set `CLAUDE_ENABLED=false` if you want webhook processing without Claude analysis.

## Docker Compose

```bash
cd /home/runner/work/DarkoTradingPlatform/DarkoTradingPlatform
docker compose --env-file .env up --build
```

- Nginx listens on `http://localhost:80`
- The .NET app listens internally on port `8080`
- SQLite data persists in the `tradingagent-data` volume

## API endpoints

- `POST /api/tradingview/webhook`
- `GET /api/signals`
- `GET /api/signals/{id}`
- `PATCH /api/signals/{id}`
- `DELETE /api/signals/{id}`

## Notes

- SQLite is created automatically on startup.
- The dashboard is intentionally simple so authentication can be added later.
- Secrets are not hardcoded and are not written to logs.