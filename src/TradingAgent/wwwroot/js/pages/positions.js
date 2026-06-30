window.Dashboard = window.Dashboard || {};
Dashboard.pages = Dashboard.pages || {};

Dashboard.pages.positions = {
    state: { page: 1, pageSize: 25, tab: 'open', filters: {} },

    async render(container) {
        container.innerHTML = this.template();
        this.bind(container);
        await this.load(container);
    },

    template() {
        return `
            <div class="panel">
                <h2>Positions</h2>
                <div class="tabs">
                    <button class="tab ${this.state.tab === 'open' ? 'active' : ''}" data-tab="open">Open Positions</button>
                    <button class="tab ${this.state.tab === 'closed' ? 'active' : ''}" data-tab="closed">Closed Positions</button>
                </div>
                <div class="filters">
                    <div><label>Symbol</label><input id="pos-symbol" placeholder="NVDA" /></div>
                    <div><label>Win/Loss</label>
                        <select id="pos-outcome"><option value="">All</option><option value="win">Win</option><option value="loss">Loss</option></select>
                    </div>
                    <div><label>From (UTC)</label><input id="pos-from" type="datetime-local" /></div>
                    <div><label>To (UTC)</label><input id="pos-to" type="datetime-local" /></div>
                    <div class="btn-row"><button id="pos-apply">Apply</button></div>
                </div>
                <div class="table-wrap" id="pos-table"></div>
                <div id="pos-pagination"></div>
            </div>`;
    },

    bind(container) {
        container.querySelectorAll('.tab').forEach(tab => {
            tab.addEventListener('click', () => {
                this.state.tab = tab.dataset.tab;
                this.state.page = 1;
                container.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.dataset.tab === this.state.tab));
                this.load(container);
            });
        });
        container.querySelector('#pos-apply')?.addEventListener('click', () => {
            this.state.page = 1;
            this.readFilters(container);
            this.load(container);
        });
    },

    readFilters(container) {
        const from = container.querySelector('#pos-from')?.value;
        const to = container.querySelector('#pos-to')?.value;
        this.state.filters = {
            symbol: container.querySelector('#pos-symbol')?.value.trim(),
            outcome: container.querySelector('#pos-outcome')?.value,
            from: from ? new Date(from).toISOString() : '',
            to: to ? new Date(to).toISOString() : ''
        };
    },

    async load(container) {
        Dashboard.utils.setStatus('Loading positions…');
        const query = Dashboard.utils.buildQuery({
            page: this.state.page,
            pageSize: this.state.pageSize,
            status: this.state.tab,
            ...this.state.filters
        });

        try {
            const data = Dashboard.utils.normalizePaged(await Dashboard.utils.fetchJson(`/api/positions${query}`));
            this.state.page = data.page;
            this.state.pageSize = data.pageSize;
            container.querySelector('#pos-table').innerHTML = this.state.tab === 'open'
                ? this.renderOpenTable(data.items)
                : this.renderClosedTable(data.items);
            Dashboard.pagination.render(container.querySelector('#pos-pagination'), data, ({ page, pageSize }) => {
                this.state.page = page;
                this.state.pageSize = pageSize;
                this.load(container);
            });
            Dashboard.utils.setStatus(`${data.total} position(s)`);
        } catch (error) {
            container.querySelector('#pos-table').innerHTML = `<p>${Dashboard.utils.escapeHtml(error.message)}</p>`;
        }
    },

    renderOpenTable(positions) {
        if (!positions.length) return '<p class="status-text">No open positions.</p>';
        return `<table class="data-table"><thead><tr>
            <th>ID</th><th>Symbol</th><th>Entry</th><th>Entry time</th><th>Session</th><th>Qty</th><th>Risk</th>
            <th>SL</th><th>TP</th><th>Notes</th><th>Actions</th>
        </tr></thead><tbody>${positions.map(p => `
            <tr>
                <td>${p.id}</td>
                <td><strong>${Dashboard.utils.escapeHtml(p.symbol)}</strong></td>
                <td>${Dashboard.utils.formatValue(p.entryPrice)}</td>
                <td>${Dashboard.utils.formatDate(p.entryTimeUtc)}</td>
                <td>${Dashboard.utils.escapeHtml(p.entryMarketSession || '—')}</td>
                <td>${Dashboard.utils.formatValue(p.quantity)}</td>
                <td>${Dashboard.utils.formatValue(p.maxRiskPercent)}%</td>
                <td>${Dashboard.utils.formatValue(p.stopLoss)}</td>
                <td>${Dashboard.utils.formatValue(p.takeProfit)}</td>
                <td class="wrap"><input id="pos-notes-${p.id}" value="${Dashboard.utils.escapeHtml(p.notes || '')}" style="min-width:120px" /></td>
                <td>
                    <button class="link-btn" onclick="Dashboard.pages.positions.saveNotes(${p.id})">Save</button>
                    <button class="link-btn danger" onclick="Dashboard.pages.positions.close(${p.id})">Close</button>
                </td>
            </tr>`).join('')}</tbody></table>`;
    },

    renderClosedTable(positions) {
        if (!positions.length) return '<p class="status-text">No closed positions.</p>';
        return `<table class="data-table"><thead><tr>
            <th>ID</th><th>Symbol</th><th>Entry</th><th>Exit</th><th>Entry time</th><th>Exit time</th>
            <th>Duration</th><th>P/L</th><th>P/L %</th><th>Result</th><th>Notes</th>
        </tr></thead><tbody>${positions.map(p => {
            const pl = p.profitLoss;
            const result = typeof pl === 'number' ? (pl >= 0 ? 'Win' : 'Loss') : '—';
            return `
            <tr>
                <td>${p.id}</td>
                <td><strong>${Dashboard.utils.escapeHtml(p.symbol)}</strong></td>
                <td>${Dashboard.utils.formatValue(p.entryPrice)}</td>
                <td>${Dashboard.utils.formatValue(p.exitPrice)}</td>
                <td>${Dashboard.utils.formatDate(p.entryTimeUtc)}</td>
                <td>${Dashboard.utils.formatDate(p.exitTimeUtc)}</td>
                <td>${Dashboard.utils.formatDuration(p.entryTimeUtc, p.exitTimeUtc)}</td>
                <td class="${Dashboard.utils.plClass(pl)}">${Dashboard.utils.formatValue(pl)}</td>
                <td class="${Dashboard.utils.plClass(pl)}">${Dashboard.utils.formatValue(p.profitLossPercent)}%</td>
                <td>${result}</td>
                <td class="wrap">${Dashboard.utils.escapeHtml(p.notes || '—')}</td>
            </tr>`;
        }).join('')}</tbody></table>`;
    },

    async saveNotes(id) {
        const notes = document.getElementById(`pos-notes-${id}`)?.value ?? '';
        await fetch(`/api/positions/${id}`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ notes })
        });
        Dashboard.utils.setStatus(`Position ${id} notes saved.`);
    },

    async close(id) {
        if (!confirm(`Close position ${id}?`)) return;
        const response = await fetch(`/api/positions/${id}/close`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({})
        });
        if (!response.ok) {
            Dashboard.utils.setStatus(`Failed to close position ${id}.`);
            return;
        }
        Dashboard.utils.setStatus(`Position ${id} closed.`);
        Dashboard.router.navigate('#/positions');
    }
};
