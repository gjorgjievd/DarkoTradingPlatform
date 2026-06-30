window.Dashboard = window.Dashboard || {};
Dashboard.pages = Dashboard.pages || {};

Dashboard.pages.webhooks = {
    state: { page: 1, pageSize: 25, filters: { filter: 'all' } },

    async render(container) {
        container.innerHTML = this.template();
        this.bind(container);
        await this.load(container);
    },

    template() {
        return `
            <div class="panel">
                <h2>Webhook History</h2>
                <div class="filters">
                    <div><label>Type</label>
                        <select id="wh-filter"><option value="all">All</option><option value="real">Real</option><option value="test">Test</option></select>
                    </div>
                    <div><label>Source</label>
                        <select id="wh-source"><option value="">All</option><option>TRADINGVIEW</option><option>POSTMAN_TEST</option><option>CURSOR_TEST</option><option>UNKNOWN</option></select>
                    </div>
                    <div><label>Result status</label>
                        <select id="wh-status"><option value="">All</option><option>SUCCESS</option><option>IGNORED</option><option>AI_FAILED</option><option>ERROR</option><option>BAD_REQUEST</option><option>UNAUTHORIZED</option></select>
                    </div>
                    <div><label>Symbol</label><input id="wh-symbol" placeholder="NVDA" /></div>
                    <div><label>From (UTC)</label><input id="wh-from" type="datetime-local" /></div>
                    <div><label>To (UTC)</label><input id="wh-to" type="datetime-local" /></div>
                    <div class="btn-row"><button id="wh-apply">Apply</button></div>
                </div>
                <div class="table-wrap" id="wh-table"></div>
                <div id="wh-pagination"></div>
            </div>`;
    },

    bind(container) {
        container.querySelector('#wh-apply')?.addEventListener('click', () => {
            this.state.page = 1;
            this.readFilters(container);
            this.load(container);
        });
    },

    readFilters(container) {
        const from = container.querySelector('#wh-from')?.value;
        const to = container.querySelector('#wh-to')?.value;
        const filter = container.querySelector('#wh-filter')?.value;
        this.state.filters = {
            filter: filter === 'all' ? '' : filter,
            source: container.querySelector('#wh-source')?.value,
            status: container.querySelector('#wh-status')?.value,
            symbol: container.querySelector('#wh-symbol')?.value.trim(),
            from: from ? new Date(from).toISOString() : '',
            to: to ? new Date(to).toISOString() : ''
        };
    },

    async load(container) {
        Dashboard.utils.setStatus('Loading webhooks…');
        const query = Dashboard.utils.buildQuery({
            page: this.state.page,
            pageSize: this.state.pageSize,
            ...this.state.filters
        });

        try {
            const data = Dashboard.utils.normalizePaged(await Dashboard.utils.fetchJson(`/api/webhooks/history${query}`));
            this.state.page = data.page;
            this.state.pageSize = data.pageSize;
            container.querySelector('#wh-table').innerHTML = this.renderTable(data.items);
            Dashboard.pagination.render(container.querySelector('#wh-pagination'), data, ({ page, pageSize }) => {
                this.state.page = page;
                this.state.pageSize = pageSize;
                this.load(container);
            });
            Dashboard.utils.setStatus(`${data.total} webhook(s)`);
        } catch (error) {
            container.querySelector('#wh-table').innerHTML = `<p>${Dashboard.utils.escapeHtml(error.message)}</p>`;
        }
    },

    renderTable(items) {
        if (!items.length) return '<p class="status-text">No webhooks found.</p>';
        return `<table class="data-table"><thead><tr>
            <th>ID</th><th>Received</th><th>Source</th><th>Test</th><th>Symbol</th><th>Signal</th>
            <th>Status</th><th>Error</th><th>Signal ID</th><th>Actions</th>
        </tr></thead><tbody>${items.map(w => `
            <tr>
                <td>${w.id}</td>
                <td>${Dashboard.utils.formatDate(w.receivedAtUtc)}</td>
                <td>${Dashboard.utils.escapeHtml(w.source)}</td>
                <td>${w.isTest ? Dashboard.utils.pill('TEST', 'test') : 'No'}</td>
                <td>${Dashboard.utils.escapeHtml(w.symbol || '—')}</td>
                <td>${Dashboard.utils.escapeHtml(w.signalType || '—')}</td>
                <td>${Dashboard.utils.escapeHtml(w.resultStatus)}</td>
                <td class="wrap">${Dashboard.utils.escapeHtml(Dashboard.utils.truncate(w.errorMessage, 40))}</td>
                <td>${w.tradingSignalId ?? '—'}</td>
                <td><button class="link-btn" onclick="Dashboard.pages.webhooks.view(${w.id})">View payload</button></td>
            </tr>`).join('')}</tbody></table>`;
    },

    async view(id) {
        try {
            const log = await Dashboard.utils.fetchJson(`/api/webhooks/history/${id}`);
            Dashboard.modal.show(`Webhook #${id}`, `
                <p><strong>Result:</strong> ${Dashboard.utils.escapeHtml(log.resultStatus)}</p>
                ${log.errorMessage ? `<p><strong>Error:</strong> ${Dashboard.utils.escapeHtml(log.errorMessage)}</p>` : ''}
                <p><strong>Raw payload</strong></p>
                <pre>${Dashboard.utils.escapeHtml(log.rawPayload || '(empty)')}</pre>
                <p><strong>Headers JSON</strong></p>
                <pre>${Dashboard.utils.escapeHtml(log.headersJson || '(none)')}</pre>
                <div class="btn-row"><button class="secondary" onclick="Dashboard.modal.close()">Close</button></div>
            `);
        } catch (error) {
            Dashboard.utils.setStatus(error.message);
        }
    }
};
