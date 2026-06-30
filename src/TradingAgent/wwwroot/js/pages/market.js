window.Dashboard = window.Dashboard || {};
Dashboard.pages = Dashboard.pages || {};

Dashboard.pages.market = {
    async render(container) {
        container.innerHTML = '<p class="status-text">Loading market data…</p>';
        Dashboard.utils.setStatus('Loading market…');

        try {
            const year = new Date().getFullYear();
            const [status, calendar] = await Promise.all([
                Dashboard.utils.fetchJson('/api/market/status'),
                Dashboard.utils.fetchJson(`/api/market/calendar?year=${year}`)
            ]);

            const upcoming = calendar
                .filter(h => new Date(h.observedDate || h.date) >= new Date())
                .slice(0, 8);

            container.innerHTML = `
                <div class="cards">${this.renderStatusCards(status)}</div>
                <div class="panel">
                    <h2>Market calendar ${year}</h2>
                    <div class="table-wrap">
                        <table class="data-table">
                            <thead><tr><th>Holiday</th><th>Date</th><th>Observed</th></tr></thead>
                            <tbody>
                                ${calendar.map(h => `
                                    <tr>
                                        <td>${Dashboard.utils.escapeHtml(h.name)}</td>
                                        <td>${Dashboard.utils.escapeHtml(h.date)}</td>
                                        <td>${Dashboard.utils.escapeHtml(h.observedDate)}</td>
                                    </tr>`).join('')}
                            </tbody>
                        </table>
                    </div>
                </div>
                <div class="panel">
                    <h2>Upcoming holidays</h2>
                    <div class="table-wrap">
                        <table class="data-table">
                            <thead><tr><th>Holiday</th><th>Observed date</th></tr></thead>
                            <tbody>
                                ${upcoming.length ? upcoming.map(h => `
                                    <tr>
                                        <td>${Dashboard.utils.escapeHtml(h.name)}</td>
                                        <td>${Dashboard.utils.escapeHtml(h.observedDate || h.date)}</td>
                                    </tr>`).join('') : '<tr><td colspan="2">No upcoming holidays.</td></tr>'}
                            </tbody>
                        </table>
                    </div>
                </div>`;

            Dashboard.utils.setStatus(`Market: ${status.marketName} · ${status.marketSession || status.status}`);
        } catch (error) {
            container.innerHTML = `<div class="panel"><p>${Dashboard.utils.escapeHtml(error.message)}</p></div>`;
            Dashboard.utils.setStatus(error.message);
        }
    },

    renderStatusCards(status) {
        const items = [
            ['Current market', status.marketName],
            ['Market session', status.marketSession || status.status],
            ['Is open', status.isOpen ? 'Yes' : 'No'],
            ['Market time', Dashboard.utils.formatDate(status.currentMarketTime)],
            ['Next open', Dashboard.utils.formatDate(status.nextOpenTimeUtc)],
            ['Next close', Dashboard.utils.formatDate(status.nextCloseTimeUtc)],
            ['Weekend', status.isWeekend ? 'Yes' : 'No'],
            ['Holiday', status.isHoliday ? 'Yes' : 'No'],
            ['Pre-market', status.isPreMarket ? 'Yes' : 'No'],
            ['After-hours', status.isAfterHours ? 'Yes' : 'No'],
            ['Overnight', status.isOvernight ? 'Yes' : 'No'],
            ['Session confidence threshold', `${status.sessionConfidenceThreshold}%`]
        ];

        return items.map(([label, value]) => `
            <div class="card compact">
                <div class="label">${label}</div>
                <div class="value">${Dashboard.utils.escapeHtml(String(value))}</div>
            </div>`).join('');
    }
};
