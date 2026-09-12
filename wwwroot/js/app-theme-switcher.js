(function () {
    var STORAGE_KEY = 'profitnx-app-theme';
    var COOKIE_KEY = 'pnx_theme';
    var PREV_LIGHT_KEY = 'profitnx-app-theme-prev-light';
    var THEMES = ['classic', 'premium', 'saas', 'dompet', 'midnight', 'emerald', 'rosegold'];
    var DARK_THEME = 'midnight';
    var LIGHT_THEMES = ['classic', 'premium', 'saas', 'dompet', 'emerald', 'rosegold'];

    var FONT_STYLE_STORAGE_KEY = 'profitnx-app-font-style';
    var FONT_STYLE_COOKIE_KEY = 'pnx_font_style';
    var FONT_STYLES = ['default', 'modern', 'elegant', 'rounded'];

    var FONT_SIZE_STORAGE_KEY = 'profitnx-app-font-size';
    var FONT_SIZE_COOKIE_KEY = 'pnx_font_size';
    var FONT_SIZES = ['sm', 'md', 'lg'];

    function readCookie(name) {
        var match = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]+)'));
        return match ? decodeURIComponent(match[1]) : '';
    }

    function writeCookie(name, value) {
        try {
            var oneYear = 60 * 60 * 24 * 365;
            document.cookie = name + '=' + encodeURIComponent(value) + ';path=/;max-age=' + oneYear + ';SameSite=Lax';
        } catch { /* ignore */ }
    }

    function currentTheme() {
        try {
            var t = localStorage.getItem(STORAGE_KEY) || readCookie(COOKIE_KEY);
            return THEMES.indexOf(t) !== -1 ? t : 'classic';
        } catch { return 'classic'; }
    }

    function currentFontStyle() {
        try {
            var f = localStorage.getItem(FONT_STYLE_STORAGE_KEY) || readCookie(FONT_STYLE_COOKIE_KEY);
            return FONT_STYLES.indexOf(f) !== -1 ? f : 'default';
        } catch { return 'default'; }
    }

    function currentFontSize() {
        try {
            var s = localStorage.getItem(FONT_SIZE_STORAGE_KEY) || readCookie(FONT_SIZE_COOKIE_KEY);
            return FONT_SIZES.indexOf(s) !== -1 ? s : 'md';
        } catch { return 'md'; }
    }

    function apply(theme) {
        var root = document.documentElement;
        if (!theme || theme === 'classic') root.removeAttribute('data-app-theme');
        else root.setAttribute('data-app-theme', theme);
        // Convenience flag for CSS / Live Dashboard
        if (theme === DARK_THEME) root.setAttribute('data-dark-mode', '1');
        else root.removeAttribute('data-dark-mode');
    }

    function applyFontStyle(style) {
        var root = document.documentElement;
        if (!style || style === 'default') root.removeAttribute('data-app-font-style');
        else root.setAttribute('data-app-font-style', style);
    }

    function applyFontSize(size) {
        var root = document.documentElement;
        if (!size || size === 'md') root.removeAttribute('data-app-font-scale');
        else root.setAttribute('data-app-font-scale', size);
    }

    function isDarkTheme(theme) {
        return theme === DARK_THEME;
    }

    function getPrevLightTheme() {
        try {
            var t = localStorage.getItem(PREV_LIGHT_KEY) || 'classic';
            return LIGHT_THEMES.indexOf(t) !== -1 ? t : 'classic';
        } catch { return 'classic'; }
    }

    function rememberLightTheme(theme) {
        if (!theme || theme === DARK_THEME) return;
        try { localStorage.setItem(PREV_LIGHT_KEY, theme); } catch { /* ignore */ }
    }

    function setThemePersisted(theme, saveDefault) {
        apply(theme);
        try { localStorage.setItem(STORAGE_KEY, theme); } catch { /* ignore */ }
        writeCookie(COOKIE_KEY, theme);
        if (saveDefault && typeof window.__pnxSaveThemeDefault === 'function') {
            window.__pnxSaveThemeDefault(theme);
        }
        syncDarkModeToggle();
        document.dispatchEvent(new CustomEvent('pnx-theme-changed', { detail: { theme: theme } }));
    }

    function toggleDarkMode(saveDefault) {
        var active = currentTheme();
        if (isDarkTheme(active)) {
            setThemePersisted(getPrevLightTheme(), saveDefault);
        } else {
            rememberLightTheme(active);
            setThemePersisted(DARK_THEME, saveDefault);
        }
    }

    function syncDarkModeToggle() {
        var dark = isDarkTheme(currentTheme());
        document.querySelectorAll('#darkModeToggle, [data-dark-mode-toggle]').forEach(function (btn) {
            btn.classList.toggle('is-dark', dark);
            btn.setAttribute('aria-pressed', dark ? 'true' : 'false');
            btn.setAttribute('title', dark ? 'Switch to light mode' : 'Switch to dark mode');
            btn.setAttribute('data-bs-title', dark ? 'Switch to light mode' : 'Switch to dark mode');
            var moon = btn.querySelector('.dark-mode-icon-moon');
            var sun = btn.querySelector('.dark-mode-icon-sun');
            if (moon) moon.style.display = dark ? 'none' : '';
            if (sun) sun.style.display = dark ? '' : 'none';
            var badge = btn.querySelector('#darkModeMenuState') || document.getElementById('darkModeMenuState');
            if (badge) badge.textContent = dark ? 'ON' : 'OFF';
            var label = btn.querySelector('.dark-mode-menu-text');
            if (label) label.textContent = dark ? 'Dark Mode (On)' : 'Dark Mode';
        });
        // Bootstrap tooltip title refresh
        document.querySelectorAll('#darkModeToggle, [data-dark-mode-toggle]').forEach(function (btn) {
            if (window.bootstrap && bootstrap.Tooltip) {
                var tip = bootstrap.Tooltip.getInstance(btn);
                if (tip) {
                    tip.setContent({ '.tooltip-inner': dark ? 'Switch to light mode' : 'Switch to dark mode' });
                }
            }
        });
    }

    // Applied immediately (this file is also referenced by an early inline call in <head>)
    apply(currentTheme());
    applyFontStyle(currentFontStyle());
    applyFontSize(currentFontSize());

    function saveAsAccountDefault(theme) {
        var form = document.getElementById('themeSaveDefaultForm');
        if (!form) return;
        try {
            var input = form.querySelector('input[name="theme"]');
            if (!input) {
                input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'theme';
                form.appendChild(input);
            }
            input.value = theme;
            // Prefer fetch so UI doesn't navigate away
            var token = form.querySelector('input[name="__RequestVerificationToken"]');
            var body = new FormData();
            body.append('theme', theme);
            if (token) body.append('__RequestVerificationToken', token.value);
            fetch(form.action, { method: 'POST', body: body, credentials: 'same-origin' }).catch(function () { /* ignore */ });
        } catch { /* ignore */ }
    }
    window.__pnxSaveThemeDefault = saveAsAccountDefault;

    document.addEventListener('DOMContentLoaded', function () {
        var btn = document.getElementById('themeSwitchBtn');
        var menu = document.getElementById('themeSwitchMenu');
        var defaultChk = document.getElementById('themeSetDefaultChk');

        function sync() {
            if (!menu) return;
            var active = currentTheme();
            menu.querySelectorAll('.theme-switch-option').forEach(function (opt) {
                opt.classList.toggle('active', (opt.dataset.theme || 'classic') === active);
            });
            var activeFontStyle = currentFontStyle();
            menu.querySelectorAll('.font-style-option').forEach(function (opt) {
                opt.classList.toggle('active', (opt.dataset.fontStyle || 'default') === activeFontStyle);
            });
            var activeFontSize = currentFontSize();
            menu.querySelectorAll('.font-size-option').forEach(function (opt) {
                opt.classList.toggle('active', (opt.dataset.fontSize || 'md') === activeFontSize);
            });
            syncDarkModeToggle();
        }
        sync();

        if (btn && menu) {
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                menu.classList.toggle('open');
            });
            document.addEventListener('click', function (e) {
                if (!menu.contains(e.target) && e.target !== btn) menu.classList.remove('open');
            });
            menu.addEventListener('click', function (e) {
                var themeOpt = e.target.closest('.theme-switch-option');
                if (themeOpt) {
                    var theme = themeOpt.dataset.theme || 'classic';
                    if (!isDarkTheme(theme)) rememberLightTheme(theme);
                    apply(theme);
                    try { localStorage.setItem(STORAGE_KEY, theme); } catch { /* ignore */ }
                    writeCookie(COOKIE_KEY, theme);
                    if (!defaultChk || defaultChk.checked) saveAsAccountDefault(theme);
                    sync();
                    menu.classList.remove('open');
                    document.dispatchEvent(new CustomEvent('pnx-theme-changed', { detail: { theme: theme } }));
                    return;
                }
                var fontStyleOpt = e.target.closest('.font-style-option');
                if (fontStyleOpt) {
                    var fontStyle = fontStyleOpt.dataset.fontStyle || 'default';
                    applyFontStyle(fontStyle);
                    try { localStorage.setItem(FONT_STYLE_STORAGE_KEY, fontStyle); } catch { /* ignore */ }
                    writeCookie(FONT_STYLE_COOKIE_KEY, fontStyle);
                    sync();
                    return;
                }
                var fontSizeOpt = e.target.closest('.font-size-option');
                if (fontSizeOpt) {
                    var fontSize = fontSizeOpt.dataset.fontSize || 'md';
                    applyFontSize(fontSize);
                    try { localStorage.setItem(FONT_SIZE_STORAGE_KEY, fontSize); } catch { /* ignore */ }
                    writeCookie(FONT_SIZE_COOKIE_KEY, fontSize);
                    sync();
                }
            });
        }

        // Dark mode toggle buttons (header + optional page-level)
        document.querySelectorAll('#darkModeToggle, [data-dark-mode-toggle]').forEach(function (toggleBtn) {
            toggleBtn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                var saveDefault = !defaultChk || defaultChk.checked;
                toggleDarkMode(saveDefault);
                if (menu) {
                    menu.querySelectorAll('.theme-switch-option').forEach(function (opt) {
                        opt.classList.toggle('active', (opt.dataset.theme || 'classic') === currentTheme());
                    });
                }
            });
        });
        syncDarkModeToggle();
    });

    // Expose for Live Dashboard / other pages
    window.ProfitNxTheme = {
        current: currentTheme,
        apply: setThemePersisted,
        toggleDark: function () { toggleDarkMode(true); },
        isDark: function () { return isDarkTheme(currentTheme()); }
    };
})();
