window.Dashboard = window.Dashboard || {};
Dashboard.pages = Dashboard.pages || {};

Dashboard.pages.home = {
    async render(container) {
        container.innerHTML = '<p class="status-text">Loading overview…</p>';
        Dashboard.utils.setStatus('Loading home…');

        try {
            const [signals, openPos, closedPos, market, webhooks] = await Promise.all([
                Dashboard.utils.fetchJson('/api/signals'),
                Dashboard.utils.fetchJson('/api/positions/open'),
                Dashboard.utils.fetchJson('/api/positions/closed'),
                Dashboard.utils.fetchJson('/api/market/status').catch(() => null),
                Dashboard.utils.fetchJson('/api/webhooks/history' + Dashboard.utils.buildQuery({ page: 1, pageSize: 5 }))
            ]);

            const webhookData = Dashboard.utils.normalizePaged(webhooks);
            const summary = this.buildSummary(signals);
            const perf = this.buildPerformance(closedPos);

            container.innerHTML = `
                <div class="cards">${this.renderMarketCard(market)}</div>
                <div class="cards">${this.renderStatCards(summary, openPos.length, closedPos.length, perf)}</div>
                <div class="panel">
                    <h2>Latest signals</h2>
                    <div class="table-wrap">${this.renderSignalsTable(signals.slice(0, 5))}</div>
                </div>
                <div class="panel">
                    <h2>Latest webhooks</h2>
                    <div class="table-wrap">${this.renderWebhooksTable(webhookData.items)}</div>
                </div>`;

            Dashboard.utils.setStatus(`Overview loaded · ${signals.length} signals`);
        } catch (error) {
            container.innerHTML = `<div class="panel"><p>${Dashboard.utils.escapeHtml(error.message)}</p></div>`;
            Dashboard.utils.setStatus(error.message);
        }
    },

    buildSummary(signals) {
        const summary = { total: signals.length, buy: 0, sell: 0, wait: 0, ignore: 0 };
        for (const signal of signals) {
            const action = (signal.claudeAction || signal.originalSignal || '').toUpperCase();
            if (action === 'BUY') summary.buy++;
            else if (action === 'SELL') summary.sell++;
            else if (action === 'WAIT') summary.wait++;
            else summary.ignore++;
        }
        return summary;
    },

    buildPerformance(closed) {
        const wins = closed.filter(p => typeof p.profitLoss === 'number' && p.profitLoss > 0);
        const losses = closed.filter(p => typeof p.profitLoss === 'number' && p.profitLoss < 0);
        const totalPl = closed.reduce((sum, p) => sum + (p.profitLoss ?? 0), 0);
        const winRate = closed.length === 0 ? 0 : (wins.length / closed.length) * 100;
        const avgWin = wins.length ? wins.reduce((s, p) => s + p.profitLoss, 0) / wins.length : 0;
        const avgLoss = losses.length ? losses.reduce((s, p) => s + p.profitLoss, 0) / losses.length : 0;
        return { totalPl, winRate, avgWin, avgLoss };
    },

    renderMarketCard(market) {
        if (!market) return '<div class="card compact"><div class="label">Market</div><div class="value">Unavailable</div></div>';
        return `
            <div class="card compact"><div class="label">Market</div><div class="value">${Dashboard.utils.escapeHtml(market.marketName)}</div></div>
            <div class="card compact"><div class="label">Session</div><div class="value">${Dashboard.utils.escapeHtml(market.marketSession || market.status)}</div></div>
            <div class="card compact"><div class="label">Status</div><div class="value">${market.isOpen ? 'Open' : 'Closed'}</div></div>
            <div class="card compact"><div class="label">Confidence threshold</div><div class="value">${market.sessionConfidenceThreshold}%</div></div>`;
    },

    renderStatCards(summary, openCount, closedCount, perf) {
        const cards = [
            ['Total trades', summary.total],
            ['BUY', summary.buy],
            ['SELL', summary.sell],
            ['WAIT', summary.wait],
            ['IGNORE', summary.ignore],
            ['Open positions', openCount],
            ['Closed positions', closedCount],
            ['Total P/L', perf.totalPl.toFixed(2)],
            ['Win rate', `${perf.winRate.toFixed(1)}%`],
            ['Avg win', perf.avgWin.toFixed(2)],
            ['Avg loss', perf.avgLoss.toFixed(2)]
        ];
        return cards.map(([label, value]) => `
            <div class="card"><div class="label">${label}</div><div class="value">${value}</div></div>`).join('');
    },

    renderSignalsTable(signals) {
        if (!signals.length) return '<p class="status-text">No signals.</p>';
        return `<table class="data-table"><thead><tr>
            <th>ID</th><th>Time</th><th>Symbol</th><th>Signal</th><th>Decision</th><th>Conf</th><th>Reason</th>
        </tr></thead><tbody>${signals.map(s => `
            <tr>
                <td>${s.id}</td>
                <td>${Dashboard.utils.formatDate(s.createdAtUtc)}</td>
                <td><strong>${Dashboard.utils.escapeHtml(s.symbol)}</strong></td>
                <td>${Dashboard.utils.escapeHtml(s.originalSignal)}</td>
                <td>${Dashboard.utils.escapeHtml(s.claudeAction || '—')}</td>
                <td>${s.confidence ?? '—'}</td>
                <td class="wrap">${Dashboard.utils.escapeHtml(Dashboard.utils.truncate(s.shortReason || s.ignoredReason, 60))}</td>
            </tr>`).join('')}</tbody></table>`;
    },

    renderWebhooksTable(items) {
        if (!items.length) return '<p class="status-text">No webhooks.</p>';
        return `<table class="data-table"><thead><tr>
            <th>ID</th><th>Time</th><th>Source</th><th>Symbol</th><th>Result</th>
        </tr></thead><tbody>${items.map(w => `
            <tr>
                <td>${w.id}</td>
                <td>${Dashboard.utils.formatDate(w.receivedAtUtc)}</td>
                <td>${Dashboard.utils.escapeHtml(w.source)}</td>
                <td>${Dashboard.utils.escapeHtml(w.symbol || '—')}</td>
                <td>${Dashboard.utils.escapeHtml(w.resultStatus)}</td>
            </tr>`).join('')}</tbody></table>`;
    }
};
