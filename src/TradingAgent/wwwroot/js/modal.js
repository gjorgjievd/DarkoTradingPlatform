window.Dashboard = window.Dashboard || {};

Dashboard.modal = {
    show(title, bodyHtml) {
        const backdrop = document.getElementById('modal-backdrop');
        const titleEl = document.getElementById('modal-title');
        const bodyEl = document.getElementById('modal-body');
        if (!backdrop || !titleEl || !bodyEl) return;

        titleEl.textContent = title;
        bodyEl.innerHTML = bodyHtml;
        backdrop.classList.remove('hidden');
    },

    close() {
        const backdrop = document.getElementById('modal-backdrop');
        if (backdrop) backdrop.classList.add('hidden');
    },

    showText(title, text) {
        this.show(title, `<pre>${Dashboard.utils.escapeHtml(text)}</pre>`);
    },

    section(title, rows) {
        const items = rows
            .filter(([, value]) => value !== undefined && value !== null && value !== '')
            .map(([label, value]) => `<li><strong>${Dashboard.utils.escapeHtml(label)}:</strong> ${value}</li>`)
            .join('');
        if (!items) return '';
        return `<div class="panel" style="margin-bottom:12px;padding:12px;"><h3 style="margin:0 0 8px;font-size:0.95rem;">${Dashboard.utils.escapeHtml(title)}</h3><ul style="margin:0;padding-left:18px;">${items}</ul></div>`;
    },

    trendLabel(a, b) {
        if (typeof a !== 'number' || typeof b !== 'number') return '—';
        if (a > b) return `${Dashboard.utils.formatValue(a)} &gt; ${Dashboard.utils.formatValue(b)}`;
        if (a < b) return `${Dashboard.utils.formatValue(a)} &lt; ${Dashboard.utils.formatValue(b)}`;
        return `${Dashboard.utils.formatValue(a)} = ${Dashboard.utils.formatValue(b)}`;
    },

    showSignalDetails(signal) {
        let tv = {};
        try {
            tv = JSON.parse(signal.rawPayload || '{}');
        } catch {
            tv = {};
        }

        const tvEma9 = Number(tv.ema9);
        const tvEma20 = Number(tv.ema20);
        const tvRsi = Number(tv.rsi);
        const tvVolumeSpike = Number(tv.volumeSpike);
        const market = signal.marketData;
        const tvPrice = signal.price;
        const yahooPrice = market?.currentPrice;
        const drift = (typeof tvPrice === 'number' && typeof yahooPrice === 'number' && yahooPrice !== 0)
            ? `${Math.abs((tvPrice - yahooPrice) / yahooPrice * 100).toFixed(2)}%`
            : '—';

        const tradingView = this.section('TradingView', [
            ['Signal', Dashboard.utils.escapeHtml(signal.originalSignal)],
            ['EMA9 vs EMA20', this.trendLabel(tvEma9, tvEma20)],
            ['RSI', Number.isFinite(tvRsi) ? Dashboard.utils.formatValue(tvRsi, 0) : '—'],
            ['Volume Spike', Number.isFinite(tvVolumeSpike) ? `${Dashboard.utils.formatValue(tvVolumeSpike, 0)}%` : '—'],
            ['Timeframe', Dashboard.utils.escapeHtml(signal.timeframe || tv.timeframe || '—')]
        ]);

        const marketValidation = this.section('Market Validation', [
            ['Current Price', Dashboard.utils.formatValue(yahooPrice)],
            ['Price Drift', drift],
            ['Session', Dashboard.utils.escapeHtml(signal.marketSession || signal.marketStatus || '—')],
            ['Market', Dashboard.utils.escapeHtml(signal.marketName || '—')]
        ]);

        const news = this.section('News / Risk Context', [
            ['News', Dashboard.utils.escapeHtml(signal.shortReason?.includes('news') ? signal.shortReason : 'No major news flagged')],
            ['Risk', Dashboard.utils.escapeHtml(signal.riskLevel || '—')],
            ['Categories', Dashboard.utils.escapeHtml(signal.reasonCategories || '—')]
        ]);

        const decision = this.section('Decision', [
            ['Confidence', signal.confidence ?? '—'],
            ['Decision', Dashboard.utils.escapeHtml(signal.claudeAction || 'N/A')],
            ['Reason', Dashboard.utils.escapeHtml(signal.shortReason || signal.ignoredReason || 'No reason captured.')]
        ]);

        this.show(`Signal #${signal.id}`, `
            <p><strong>${Dashboard.utils.escapeHtml(signal.symbol)}</strong></p>
            ${tradingView}
            ${marketValidation}
            ${news}
            ${decision}
            <p><strong>Notes</strong></p>
            <textarea id="modal-signal-notes" rows="3" style="width:100%">${Dashboard.utils.escapeHtml(signal.notes || '')}</textarea>
            <div class="btn-row" style="margin-top:12px">
                <button onclick="Dashboard.pages.signals.saveFromModal(${signal.id})">Save notes</button>
                <button class="secondary" onclick="Dashboard.modal.close()">Close</button>
            </div>
        `);
    }
};

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('modal-close')?.addEventListener('click', () => Dashboard.modal.close());
    document.getElementById('modal-backdrop')?.addEventListener('click', (event) => {
        if (event.target.id === 'modal-backdrop') Dashboard.modal.close();
    });
});
