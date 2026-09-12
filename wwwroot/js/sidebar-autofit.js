(function () {
    // Sidebar auto-fit: the CRM sidebar lists a different number of menu
    // items per role (Admin sees the most). Instead of scrolling, this
    // measures the menu against the available height and shrinks a single
    // --menu-scale CSS variable (font size / padding / gaps, see
    // app-theme-switcher.css) in small steps until everything fits on one
    // screen, with a sensible minimum so text never becomes unreadable.
    var MIN_SCALE = 0.62;
    var STEP = 0.04;

    function fit() {
        var rail = document.querySelector('.side-rail');
        var list = rail ? rail.querySelector('.menu-list') : null;
        if (!rail || !list) return;

        rail.style.setProperty('--menu-scale', 1);

        // Available height = sidebar's own content box minus the brand
        // block above the menu (and a little breathing room).
        var railStyle = getComputedStyle(rail);
        var padTop = parseFloat(railStyle.paddingTop) || 0;
        var padBottom = parseFloat(railStyle.paddingBottom) || 0;
        var brand = rail.querySelector('.brand-block');
        var brandHeight = brand ? brand.getBoundingClientRect().height + 24 : 0;
        var available = rail.clientHeight - padTop - padBottom - brandHeight - 8;

        var scale = 1;
        var guard = 0;
        while (list.scrollHeight > available && scale > MIN_SCALE && guard < 20) {
            scale = Math.max(MIN_SCALE, scale - STEP);
            rail.style.setProperty('--menu-scale', scale.toFixed(2));
            guard++;
        }
    }

    var raf = null;
    function scheduleFit() {
        if (raf) cancelAnimationFrame(raf);
        raf = requestAnimationFrame(fit);
    }

    document.addEventListener('DOMContentLoaded', scheduleFit);
    window.addEventListener('resize', scheduleFit);
    window.addEventListener('load', scheduleFit);
})();
