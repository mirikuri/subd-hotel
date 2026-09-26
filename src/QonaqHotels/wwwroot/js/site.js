'use strict';

// ── 1. Header scroll effect ────────────────────────────────
(function () {
    const header   = document.getElementById('site-header');
    const sentinel = document.getElementById('header-sentinel');
    if (!header || !sentinel) return;
    const obs = new IntersectionObserver(
        ([entry]) => header.classList.toggle('scrolled', !entry.isIntersecting),
        { threshold: 0 }
    );
    obs.observe(sentinel);
})();

// ── 2. Burger menu ────────────────────────────────────────
(function () {
    const burger = document.getElementById('burger');
    const nav    = document.getElementById('nav-links');
    if (!burger || !nav) return;
    burger.addEventListener('click', () => nav.classList.toggle('open'));
})();

// ── 3. Show/hide password ─────────────────────────────────
document.querySelectorAll('[data-toggle-password]').forEach(btn => {
    btn.addEventListener('click', () => {
        const input = btn.closest('.password-wrapper')?.querySelector('input');
        if (!input) return;
        const isPassword = input.type === 'password';
        input.type = isPassword ? 'text' : 'password';
        btn.querySelector('i').className = isPassword ? 'bi bi-eye-slash' : 'bi bi-eye';
    });
});

// ── 4. Caps Lock warning ──────────────────────────────────
document.querySelectorAll('[data-capslock-warning]').forEach(input => {
    const warning = document.getElementById(input.dataset.capslockWarning);
    if (!warning) return;
    const update = (e) => warning.classList.toggle('visible', e.getModifierState?.('CapsLock') ?? false);
    input.addEventListener('keydown', update);
    input.addEventListener('keyup',  update);
});

// ── 5. Search date validation ─────────────────────────────
(function () {
    const checkIn  = document.getElementById('search-checkin');
    const checkOut = document.getElementById('search-checkout');
    if (!checkIn || !checkOut) return;

    const today    = new Date();
    const tomorrow = new Date(today);
    tomorrow.setDate(today.getDate() + 1);
    const fmt = d => d.toISOString().slice(0, 10);

    checkIn.min  = fmt(today);
    checkOut.min = fmt(tomorrow);
    if (!checkIn.value)  checkIn.value  = fmt(today);
    if (!checkOut.value) checkOut.value = fmt(tomorrow);

    checkIn.addEventListener('change', () => {
        if (!checkIn.value) return;
        const minOut = new Date(checkIn.value);
        minOut.setDate(minOut.getDate() + 1);
        checkOut.min = fmt(minOut);
        if (checkOut.value && checkOut.value <= checkIn.value)
            checkOut.value = fmt(minOut);
    });
})();

// ── 6. Login spinner ──────────────────────────────────────
(function () {
    const form = document.getElementById('login-form');
    if (!form) return;
    form.addEventListener('submit', () => {
        const btn = form.querySelector('[type=submit]');
        if (!btn) return;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>Вход…';
    });
})();

// ── 7. Dropdown close on outside click ───────────────────
document.addEventListener('click', e => {
    const btn = document.getElementById('user-dropdown-btn');
    const menu = document.getElementById('user-dropdown-menu');
    if (!btn || !menu) return;
    if (btn.contains(e.target)) {
        menu.style.display = menu.style.display === 'block' ? 'none' : 'block';
    } else if (!menu.contains(e.target)) {
        menu.style.display = 'none';
    }
});
