window.Dashboard = window.Dashboard || {};
Dashboard.pages = Dashboard.pages || {};

Dashboard.pages.settings = {
    fieldMeta: [
        { key: 'CLAUDE_ENABLED', label: 'Claude enabled', type: 'bool' },
        { key: 'CLAUDE_MODEL', label: 'Claude model', type: 'text' },
        { key: 'MIN_CONFIDENCE_TO_NOTIFY', label: 'Min confidence (notify)', type: 'number', min: 0, max: 100 },
        { key: 'MIN_CONFIDENCE_REGULAR', label: 'Min confidence (regular)', type: 'number', min: 0, max: 100 },
        { key: 'MIN_CONFIDENCE_PREMARKET', label: 'Min confidence (pre-market)', type: 'number', min: 0, max: 100 },
        { key: 'MIN_CONFIDENCE_AFTER_HOURS', label: 'Min confidence (after-hours)', type: 'number', min: 0, max: 100 },
        { key: 'MIN_CONFIDENCE_OVERNIGHT', label: 'Min confidence (overnight)', type: 'number', min: 0, max: 100 },
        { key: 'ENABLE_24_5_TRADING', label: '24/5 trading enabled', type: 'bool' },
        { key: 'ALLOW_PREMARKET', label: 'Allow pre-market', type: 'bool' },
        { key: 'ALLOW_AFTER_HOURS', label: 'Allow after-hours', type: 'bool' },
        { key: 'ALLOW_OVERNIGHT', label: 'Allow overnight', type: 'bool' },
        { key: 'PAPER_TRADING_ENABLED', label: 'Paper trading enabled', type: 'bool' },
        { key: 'ALLOW_SCALE_IN', label: 'Allow scale in', type: 'bool' },
        { key: 'MAX_POSITIONS_PER_SYMBOL', label: 'Max positions per symbol', type: 'number', min: 1, max: 20 },
        { key: 'SEND_IGNORED_SIGNALS', label: 'Send ignored signals', type: 'bool' },
        { key: 'SEND_WAIT_SIGNALS', label: 'Send WAIT signals', type: 'bool' },
        { key: 'MAX_PRICE_DRIFT_PERCENT_REGULAR', label: 'Max price drift % (regular)', type: 'number', min: 0.1, max: 10, step: 0.1 },
        { key: 'MAX_PRICE_DRIFT_PERCENT_EXTENDED', label: 'Max price drift % (extended)', type: 'number', min: 0.1, max: 15, step: 0.1 },
        { key: 'SEND_TEST_TELEGRAM', label: 'Send test telegram', type: 'bool' },
        { key: 'WEBHOOK_SECRET', label: 'Webhook secret', type: 'secret' },
        { key: 'CLAUDE_TIMEOUT_SECONDS', label: 'Claude timeout (seconds)', type: 'number', min: 10, max: 300 },
        { key: 'CLAUDE_MAX_RETRIES', label: 'Claude max retries', type: 'number', min: 0, max: 5 },
        { key: 'MARKET_PROVIDER', label: 'Market provider', type: 'text' },
        { key: 'MARKET_TIMEZONE', label: 'Market timezone', type: 'text' }
    ],

    readOnlyMeta: [
        { key: 'DEFAULT_POSITION_QUANTITY', label: 'Default position quantity' },
        { key: 'ALLOW_TEST_TRADES', label: 'Allow test trades' },
        { key: 'IGNORE_SIGNALS_WHEN_MARKET_CLOSED', label: 'Ignore signals when market closed' },
        { key: 'SEND_MARKET_CLOSED_NOTIFICATIONS', label: 'Send market closed notifications' },
        { key: 'SEND_DUPLICATE_BUY_NOTIFICATIONS', label: 'Send duplicate buy notifications' }
    ],

    async render(container) {
        container.innerHTML = '<p class="status-text">Loading settings…</p>';

        try {
            const payload = await Dashboard.utils.fetchJson('/api/settings');
            const settings = payload.settings ?? payload;
            const overridden = new Set((payload.overriddenKeys ?? []).map(k => k.toUpperCase()));
            container.innerHTML = this.template(settings, overridden);
            this.bind(container, settings);
            Dashboard.utils.setStatus('Settings loaded');
        } catch (error) {
            container.innerHTML = `<div class="panel"><p>${Dashboard.utils.escapeHtml(error.message)}</p></div>`;
            Dashboard.utils.setStatus(error.message);
        }
    },

    template(settings, overridden) {
        const editableRows = this.fieldMeta.map(field => {
            const value = settings[field.key];
            const source = overridden.has(field.key)
                ? '<span class="pill warn">DB override</span>'
                : '<span class="pill">ENV default</span>';
            let input;
            if (field.type === 'bool') {
                input = `<select data-key="${field.key}">
                    <option value="true" ${value === true ? 'selected' : ''}>Yes</option>
                    <option value="false" ${value === false ? 'selected' : ''}>No</option>
                </select>`;
            } else if (field.type === 'secret') {
                input = `<input data-key="${field.key}" type="password" placeholder="${Dashboard.utils.escapeHtml(String(value ?? ''))}" />`;
            } else {
                const attrs = field.min != null ? `min="${field.min}" max="${field.max}"${field.step ? ` step="${field.step}"` : ''}` : '';
                input = `<input data-key="${field.key}" type="${field.type}" value="${Dashboard.utils.escapeHtml(String(value ?? ''))}" ${attrs} />`;
            }

            return `<tr>
                <td>${Dashboard.utils.escapeHtml(field.label)}<br />${source}</td>
                <td>${input}</td>
            </tr>`;
        }).join('');

        const readOnlyRows = this.readOnlyMeta.map(field => `
            <tr>
                <td>${Dashboard.utils.escapeHtml(field.label)}</td>
                <td>${this.formatReadOnly(settings[field.key])} <span class="pill">ENV only</span></td>
            </tr>`).join('');

        return `
            <div class="panel">
                <h2>Runtime settings</h2>
                <p class="status-text">
                    Changes are saved to the database and apply immediately.
                    <code>.env</code> is not modified. On restart, env values load first, then database overrides are applied.
                </p>
                <form id="settings-form">
                    <div class="table-wrap">
                        <table class="data-table">
                            <thead><tr><th>Setting</th><th>Value</th></tr></thead>
                            <tbody>${editableRows}</tbody>
                        </table>
                    </div>
                    <div class="btn-row" style="margin-top:12px">
                        <button type="submit">Save changes</button>
                    </div>
                </form>
            </div>
            <div class="panel">
                <h2>Environment-only settings</h2>
                <div class="table-wrap">
                    <table class="data-table">
                        <thead><tr><th>Setting</th><th>Value</th></tr></thead>
                        <tbody>${readOnlyRows}</tbody>
                    </table>
                </div>
            </div>`;
    },

    bind(container) {
        container.querySelector('#settings-form')?.addEventListener('submit', async (event) => {
            event.preventDefault();
            await this.save(container);
        });
    },

    collectUpdates(container) {
        const updates = {};
        container.querySelectorAll('[data-key]').forEach(el => {
            const key = el.dataset.key;
            const value = el.value?.trim() ?? '';
            if (el.type === 'password' && !value) {
                return;
            }
            updates[key] = value;
        });
        return updates;
    },

    async save(container) {
        const updates = this.collectUpdates(container);
        if (Object.keys(updates).length === 0) {
            Dashboard.utils.setStatus('No changes to save.');
            return;
        }

        Dashboard.utils.setStatus('Saving settings…');
        const response = await fetch('/api/settings', {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ settings: updates })
        });

        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            Dashboard.utils.setStatus(error.error || 'Failed to save settings.');
            return;
        }

        Dashboard.utils.setStatus('Settings saved and applied.');
        await this.render(container);
    },

    formatReadOnly(value) {
        if (typeof value === 'boolean') return value ? 'Yes' : 'No';
        if (value === null || value === undefined || value === '') return '—';
        return Dashboard.utils.escapeHtml(String(value));
    }
};
