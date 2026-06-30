window.Dashboard = window.Dashboard || {};

Dashboard.utils = {
    formatDate(value) {
        return value ? new Date(value).toLocaleString() : '—';
    },

    formatValue(value, digits = 2) {
        return typeof value === 'number' ? value.toFixed(digits) : '—';
    },

    escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;');
    },

    truncate(text, max = 80) {
        const value = String(text ?? '');
        if (value.length <= max) return value;
        return `${value.slice(0, max)}…`;
    },

    buildQuery(params) {
        const search = new URLSearchParams();
        Object.entries(params).forEach(([key, value]) => {
            if (value !== undefined && value !== null && value !== '') {
                search.set(key, value);
            }
        });
        const query = search.toString();
        return query ? `?${query}` : '';
    },

    async fetchJson(url) {
        const response = await fetch(url);
        if (!response.ok) {
            throw new Error(`Request failed: ${response.status}`);
        }
        return response.json();
    },

    normalizePaged(data) {
        if (Array.isArray(data)) {
            return { items: data, page: 1, pageSize: data.length, total: data.length, totalPages: 1 };
        }
        return {
            items: data.items ?? [],
            page: data.page ?? 1,
            pageSize: data.pageSize ?? 25,
            total: data.total ?? 0,
            totalPages: data.totalPages ?? 0
        };
    },

    formatDuration(start, end) {
        if (!start || !end) return '—';
        const ms = new Date(end) - new Date(start);
        const hours = Math.floor(ms / 3600000);
        const mins = Math.floor((ms % 3600000) / 60000);
        if (hours > 24) return `${Math.floor(hours / 24)}d ${hours % 24}h`;
        if (hours > 0) return `${hours}h ${mins}m`;
        return `${mins}m`;
    },

    pill(text, className = '') {
        return `<span class="pill ${className}">${Dashboard.utils.escapeHtml(text)}</span>`;
    },

    plClass(value) {
        if (typeof value !== 'number') return '';
        return value >= 0 ? 'pl-positive' : 'pl-negative';
    },

    setStatus(message) {
        const el = document.getElementById('status-text');
        if (el) el.textContent = message ?? '';
    }
};
