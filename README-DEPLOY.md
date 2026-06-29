# TradingAgent — Deployment Package

This folder contains everything needed to run TradingAgent on a machine with Docker installed. No source code is included.

## Contents

| File | Purpose |
|------|---------|
| `tradingagent-image.tar` | Pre-built Docker image (`tradingagent:latest`) |
| `docker-compose.yml` | Starts TradingAgent + Nginx |
| `nginx.conf` | Reverse proxy (port 8085 → app) |
| `.env.example` | Environment variable template |
| `load-and-run.ps1` | Windows quick-start script |
| `load-and-run.sh` | Linux/macOS quick-start script |

## Quick start (Windows)

1. Extract `TradingAgentDeploy.zip`
2. Open PowerShell in the `deploy-package` folder
3. Run:

```powershell
.\load-and-run.ps1
```

## Quick start (Linux)

```bash
chmod +x load-and-run.sh
./load-and-run.sh
```

## Manual start

```bash
docker load -i tradingagent-image.tar
cp .env.example .env    # edit .env with your secrets
docker compose --env-file .env up -d
```

Dashboard: **http://localhost:8085**

## Required environment variables

Edit `.env` after copying from `.env.example`:

| Variable | Required | Description |
|----------|----------|-------------|
| `WEBHOOK_SECRET` | Yes | Shared secret for TradingView webhooks |
| `TELEGRAM_BOT_TOKEN` | No | Telegram bot token |
| `TELEGRAM_CHAT_ID` | No | Telegram chat ID |
| `CLAUDE_API_KEY` | No | Anthropic API key |

See `.env.example` for the full list including market status settings.

## Stop / remove

```bash
docker compose --env-file .env down
```

Data persists in the `tradingagent-data` Docker volume.

## Troubleshooting

- **Port 8085 in use** — change the host port in `docker-compose.yml` (`8085:80`)
- **App won't start** — ensure `WEBHOOK_SECRET` is set in `.env`
- **Reload image** — `docker compose down` then re-run `load-and-run` script
