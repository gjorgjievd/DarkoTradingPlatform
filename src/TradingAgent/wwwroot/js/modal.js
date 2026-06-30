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

    showSignalDetails(signal) {
        const reason = signal.shortReason || signal.ignoredReason || 'No reason captured.';
        const market = signal.marketData;
        const marketHtml = market ? `
            <p><strong>Market data</strong></p>
            <ul>
                <li>Price: ${Dashboard.utils.formatValue(market.currentPrice)}</li>
                <li>RSI14: ${Dashboard.utils.formatValue(market.rsi14)}</li>
                <li>EMA9/20/50: ${Dashboard.utils.formatValue(market.ema9)} / ${Dashboard.utils.formatValue(market.ema20)} / ${Dashboard.utils.formatValue(market.ema50)}</li>
            </ul>` : '<p>No market data.</p>';

        this.show(`Signal #${signal.id}`, `
            <p><strong>${Dashboard.utils.escapeHtml(signal.symbol)}</strong> · ${Dashboard.utils.escapeHtml(signal.originalSignal)} → ${Dashboard.utils.escapeHtml(signal.claudeAction || 'N/A')}</p>
            <p>${Dashboard.utils.escapeHtml(reason)}</p>
            ${signal.reasonCategories ? `<p><strong>Category:</strong> ${Dashboard.utils.escapeHtml(signal.reasonCategories)}</p>` : ''}
            ${marketHtml}
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
