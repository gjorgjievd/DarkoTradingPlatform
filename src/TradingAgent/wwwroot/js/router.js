window.Dashboard = window.Dashboard || {};

Dashboard.router = {
    routes: {
        '/home': { title: 'Home', page: 'home' },
        '/signals': { title: 'Signals', page: 'signals' },
        '/positions': { title: 'Positions', page: 'positions' },
        '/webhooks': { title: 'Webhooks', page: 'webhooks' },
        '/market': { title: 'Market', page: 'market' },
        '/settings': { title: 'Settings', page: 'settings' }
    },

    init() {
        if (!location.hash) {
            location.hash = '#/home';
        }

        window.addEventListener('hashchange', () => this.navigate(location.hash));
        document.querySelectorAll('[data-route]').forEach(link => {
            link.addEventListener('click', (event) => {
                event.preventDefault();
                const route = link.getAttribute('data-route');
                location.hash = route;
            });
        });
        this.navigate(location.hash || '#/home');
    },

    navigate(hash) {
        const path = this.parseHash(hash);
        const route = this.routes[path] ?? this.routes['/home'];
        const container = document.getElementById('page-content');
        const title = document.getElementById('page-title');

        if (title) title.textContent = route.title;
        document.querySelectorAll('[data-route]').forEach(link => {
            link.classList.toggle('active', link.getAttribute('data-route') === path);
        });

        const page = Dashboard.pages[route.page];
        if (page && container) {
            page.render(container);
        }
    },

    parseHash(hash) {
        const cleaned = (hash || '#/home').replace(/^#/, '');
        const path = cleaned.startsWith('/') ? cleaned : `/${cleaned}`;
        return this.routes[path] ? path : '/home';
    }
};
