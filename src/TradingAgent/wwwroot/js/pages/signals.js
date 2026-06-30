window.Dashboard = window.Dashboard || {};
Dashboard.pages = Dashboard.pages || {};

Dashboard.pages.signals = {
    state: { page: 1, pageSize: 25, filters: {} },

    async render(container) {
        container.innerHTML = this.template();
        this.bind(container);
        await this.load(container);
    },

    template() {
        return `
            <div class="panel">
                <h2>Signals</h2>
                <div class="filters">
                    <div><label>Symbol</label><input id="sig-symbol" placeholder="NVDA" /></div>
                    <div><label>Signal type</label>
                        <select id="sig-signal"><option value="">All</option><option>BUY</option><option>SELL</option><option>WAIT</option></select>
                    </div>
                    <div><label>Claude decision</label>
                        <select id="sig-decision"><option value="">All</option><option>BUY</option><option>SELL</option><option>WAIT</option><option>IGNORE</option></select>
                    </div>
                    <div><label>Market session</label>
                        <select id="sig-session"><option value="">All</option><option>REGULAR</option><option>PRE_MARKET</option><option>AFTER_HOURS</option><option>OVERNIGHT</option><option>WEEKEND</option><option>HOLIDAY</option></select>
                    </div>
                    <div><label>Notified</label>
                        <select id="sig-notified"><option value="">All</option><option value="true">Yes</option><option value="false">No</option></select>
                    </div>
                    <div><label>From (UTC)</label><input id="sig-from" type="datetime-local" /></div>
                    <div><label>To (UTC)</label><input id="sig-to" type="datetime-local" /></div>
                    <div class="btn-row"><button id="sig-apply">Apply</button><button class="secondary" id="sig-reset">Reset</button></div>
                </div>
                <div class="table-wrap" id="sig-table"></div>
                <div id="sig-pagination"></div>
            </div>`;
    },

    bind(container) {
        container.querySelector('#sig-apply')?.addEventListener('click', () => {
            this.state.page = 1;
            this.readFilters(container);
            this.load(container);
        });
        container.querySelector('#sig-reset')?.addEventListener('click', () => {
            this.state = { page: 1, pageSize: 25, filters: {} };
            container.querySelectorAll('input, select').forEach(el => { if (el.tagName === 'SELECT') el.selectedIndex = 0; else el.value = ''; });
            this.load(container);
        });
    },

    readFilters(container) {
        const from = container.querySelector('#sig-from')?.value;
        const to = container.querySelector('#sig-to')?.value;
        this.state.filters = {
            symbol: container.querySelector('#sig-symbol')?.value.trim(),
            signal: container.querySelector('#sig-signal')?.value,
            decision: container.querySelector('#sig-decision')?.value,
            session: container.querySelector('#sig-session')?.value,
            notified: container.querySelector('#sig-notified')?.value,
            from: from ? new Date(from).toISOString() : '',
            to: to ? new Date(to).toISOString() : ''
        };
    },

    async load(container) {
        Dashboard.utils.setStatus('Loading signals…');
        const query = Dashboard.utils.buildQuery({
            page: this.state.page,
            pageSize: this.state.pageSize,
            ...this.state.filters
        });

        try {
            const data = Dashboard.utils.normalizePaged(await Dashboard.utils.fetchJson(`/api/signals${query}`));
            this.state.page = data.page;
            this.state.pageSize = data.pageSize;
            container.querySelector('#sig-table').innerHTML = this.renderTable(data.items);
            Dashboard.pagination.render(container.querySelector('#sig-pagination'), data, ({ page, pageSize }) => {
                this.state.page = page;
                this.state.pageSize = pageSize;
                this.load(container);
            });
            Dashboard.utils.setStatus(`${data.total} signal(s)`);
        } catch (error) {
            container.querySelector('#sig-table').innerHTML = `<p>${Dashboard.utils.escapeHtml(error.message)}</p>`;
        }
    },

    renderTable(signals) {
        if (!signals.length) return '<p class="status-text">No signals found.</p>';
        return `<table class="data-table"><thead><tr>
            <th>ID</th><th>Time</th><th>Symbol</th><th>TV Signal</th><th>Claude</th><th>Conf</th><th>Risk</th>
            <th>Session</th><th>Status</th><th>Notified</th><th>Ignored</th><th>AI Category</th><th>Price</th><th>SL</th><th>TP</th><th>R/R</th><th>Actions</th>
        </tr></thead><tbody>${signals.map(s => `
            <tr>
                <td>${s.id}</td>
                <td>${Dashboard.utils.formatDate(s.createdAtUtc)}</td>
                <td><strong>${Dashboard.utils.escapeHtml(s.symbol)}</strong>${s.isTest ? ' ' + Dashboard.utils.pill('TEST', 'test') : ''}</td>
                <td>${Dashboard.utils.escapeHtml(s.originalSignal)}</td>
                <td>${Dashboard.utils.escapeHtml(s.claudeAction || '—')}</td>
                <td>${s.confidence ?? '—'}</td>
                <td>${Dashboard.utils.escapeHtml(s.riskLevel || '—')}</td>
                <td>${Dashboard.utils.escapeHtml(s.marketSession || '—')}</td>
                <td>${Dashboard.utils.escapeHtml(s.marketStatus || '—')}</td>
                <td>${s.notified ? 'Yes' : 'No'}</td>
                <td class="wrap">${Dashboard.utils.escapeHtml(Dashboard.utils.truncate(s.ignoredReason, 40))}</td>
                <td>${Dashboard.utils.escapeHtml(s.reasonCategories || '—')}</td>
                <td>${Dashboard.utils.formatValue(s.price)}</td>
                <td>${Dashboard.utils.formatValue(s.suggestedStopLoss)}</td>
                <td>${Dashboard.utils.formatValue(s.suggestedTakeProfit)}</td>
                <td>${Dashboard.utils.formatValue(s.riskRewardRatio)}</td>
                <td>
                    <button class="link-btn" onclick="Dashboard.pages.signals.view(${s.id})">Details</button>
                    <button class="link-btn danger" onclick="Dashboard.pages.signals.remove(${s.id})">Delete</button>
                </td>
            </tr>`).join('')}</tbody></table>`;
    },

    async view(id) {
        try {
            const signal = await Dashboard.utils.fetchJson(`/api/signals/${id}`);
            Dashboard.modal.showSignalDetails(signal);
        } catch (error) {
            Dashboard.utils.setStatus(error.message);
        }
    },

    async saveFromModal(id) {
        const notes = document.getElementById('modal-signal-notes')?.value ?? '';
        await fetch(`/api/signals/${id}`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ notes })
        });
        Dashboard.modal.close();
        Dashboard.utils.setStatus(`Signal ${id} updated.`);
        Dashboard.router.navigate(location.hash || '#/signals');
    },

    async remove(id) {
        if (!confirm(`Delete signal ${id}?`)) return;
        await fetch(`/api/signals/${id}`, { method: 'DELETE' });
        Dashboard.utils.setStatus(`Signal ${id} deleted.`);
        Dashboard.router.navigate('#/signals');
    }
};
