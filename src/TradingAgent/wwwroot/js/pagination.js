window.Dashboard = window.Dashboard || {};

Dashboard.pagination = {
    render(container, state, onChange) {
        const { page, pageSize, total, totalPages } = state;
        container.innerHTML = `
            <div class="pagination">
                <div class="btn-row">
                    <label style="width:auto;margin:0">Page size</label>
                    <select id="page-size-select" style="width:auto">
                        ${[10, 25, 50, 100].map(size => `<option value="${size}" ${size === pageSize ? 'selected' : ''}>${size}</option>`).join('')}
                    </select>
                    <button class="secondary" data-page="prev" ${page <= 1 ? 'disabled' : ''}>Previous</button>
                    <span>Page ${page} / ${Math.max(totalPages, 1)}</span>
                    <button class="secondary" data-page="next" ${page >= totalPages ? 'disabled' : ''}>Next</button>
                </div>
                <div class="info">${total} result(s)</div>
            </div>`;

        container.querySelector('#page-size-select')?.addEventListener('change', (event) => {
            onChange({ page: 1, pageSize: Number(event.target.value) });
        });

        container.querySelector('[data-page="prev"]')?.addEventListener('click', () => {
            if (page > 1) onChange({ page: page - 1, pageSize });
        });

        container.querySelector('[data-page="next"]')?.addEventListener('click', () => {
            if (page < totalPages) onChange({ page: page + 1, pageSize });
        });
    }
};
