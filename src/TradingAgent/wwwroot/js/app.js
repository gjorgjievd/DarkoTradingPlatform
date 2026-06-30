document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('sidebar-toggle')?.addEventListener('click', () => {
        document.getElementById('sidebar')?.classList.toggle('open');
    });

    Dashboard.router.init();
});
