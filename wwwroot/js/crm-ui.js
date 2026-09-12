(function () {
  'use strict';

  window.ProfitNxCRM = window.ProfitNxCRM || {};

  // ========== Desktop notifications (must work when minimized — Chrome + Edge + Firefox) ==========
  window.ProfitNxDesktopNotify = {
    _swReady: null,
    ensureSw: function () {
      if (!('serviceWorker' in navigator)) return Promise.resolve(null);
      if (!this._swReady) {
        this._swReady = navigator.serviceWorker.register('/service-worker.js', { scope: '/' })
          .then(function (reg) {
            // Chrome: claim clients so controller is set without full reload
            if (reg && reg.active) {
              try { reg.active.postMessage({ type: 'SKIP_WAITING' }); } catch (_) {}
            }
            return navigator.serviceWorker.ready.then(function () { return reg; });
          })
          .catch(function () { return null; });
      }
      return this._swReady;
    },
    isBackground: function () {
      try {
        // Chrome can keep hasFocus() true while the window is minimized on Windows;
        // document.hidden / visibilityState are the reliable signals across Edge+Chrome.
        if (document.hidden) return true;
        if (document.visibilityState === 'hidden') return true;
        if (typeof document.hasFocus === 'function' && !document.hasFocus()) return true;
        // Page Lifecycle API (Chrome): discarded tab
        if (document.wasDiscarded) return true;
        return false;
      } catch (_) { return true; }
    },
    permission: function () {
      if (!('Notification' in window)) return 'unsupported';
      return Notification.permission;
    },
    requestPermission: function () {
      if (!('Notification' in window)) return Promise.resolve('unsupported');
      if (Notification.permission === 'granted') return Promise.resolve('granted');
      if (Notification.permission === 'denied') return Promise.resolve('denied');
      // Must be called from a user gesture in Chrome; callers already gate on click/keydown.
      return Notification.requestPermission().catch(function () { return Notification.permission; });
    },
    /**
     * Show an OS desktop notification.
     * opts: { title, body, url, tag, force }
     * force=true → show even if tab is focused (user asked for notifications "anyhow")
     * Works in Chrome, Edge, Firefox (HTTPS or localhost required).
     */
    show: function (opts) {
      opts = opts || {};
      var self = this;
      if (!('Notification' in window)) return Promise.resolve(false);
      if (!opts.force && !self.isBackground()) return Promise.resolve(false);

      var title = opts.title || 'ProfitNx CRM';
      var body = opts.body || '';
      var url = opts.url || '/';
      var tag = opts.tag || ('profitnx-' + Date.now());
      var icon = opts.icon || '/icons/icon-192.png';

      function paint(permission) {
        if (permission !== 'granted') return false;
        var nopts = {
          body: body,
          icon: icon,
          badge: '/icons/icon-192.png',
          tag: tag,
          renotify: true,
          requireInteraction: true,
          silent: false,
          data: { url: url }
        };

        // A) Service Worker showNotification — most reliable when tab is minimized/frozen
        //    (Chrome throttles page timers; SW path still works). Prefer registration.showNotification
        //    over postMessage so it works even before the SW controller is claimed.
        self.ensureSw().then(function (reg) {
          try {
            if (reg && typeof reg.showNotification === 'function') {
              reg.showNotification(title, nopts).catch(function () { /* ignore */ });
            }
          } catch (_) {}
          try {
            if (navigator.serviceWorker && navigator.serviceWorker.controller) {
              navigator.serviceWorker.controller.postMessage({
                type: 'SHOW_NOTIFICATION',
                title: title,
                body: body,
                icon: icon,
                tag: tag,
                url: url
              });
            }
          } catch (_) {}
        }).catch(function () { /* ignore */ });

        // B) Page Notification API (parallel) — works in Edge/Chrome when tab is still alive
        try {
          var n = new Notification(title, nopts);
          n.onclick = function () {
            try { window.focus(); } catch (_) {}
            try {
              if (url) window.location.href = url;
            } catch (_) {}
            try { n.close(); } catch (_) {}
          };
        } catch (_) {}
        return true;
      }

      if (Notification.permission === 'granted') {
        return Promise.resolve(paint('granted'));
      }
      if (Notification.permission === 'denied') {
        return Promise.resolve(false);
      }
      // permission default — request then show (may fail without user gesture in Chrome)
      return self.requestPermission().then(function (p) { return paint(p); });
    },

    urlBase64ToUint8Array: function (base64String) {
      var padding = '='.repeat((4 - (base64String.length % 4)) % 4);
      var base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
      var raw = atob(base64);
      var out = new Uint8Array(raw.length);
      for (var i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
      return out;
    },
    /** Register Web Push subscription with the CRM server (works when tab is closed). */
    subscribePush: function () {
      var self = this;
      if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) {
        return Promise.resolve({ ok: false, reason: 'unsupported' });
      }
      if (self._pushBusy) return self._pushBusy;
      self._pushBusy = (async function () {
        try {
          await self.ensureSw();
          var keyRes = await fetch('/Push/PublicKey', {
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
          });
          if (!keyRes.ok) return { ok: false, reason: 'publicKey-http-' + keyRes.status };
          var keyData = await keyRes.json();
          if (!keyData.configured || !keyData.publicKey) {
            console.warn('[ProfitNx push] Server VAPID not configured');
            return { ok: false, reason: 'not-configured' };
          }

          if (Notification.permission === 'denied') return { ok: false, reason: 'denied' };
          if (Notification.permission !== 'granted') {
            var perm = await self.requestPermission();
            if (perm !== 'granted') return { ok: false, reason: perm };
          }

          var reg = await navigator.serviceWorker.ready;
          var sub = await reg.pushManager.getSubscription();
          if (!sub) {
            sub = await reg.pushManager.subscribe({
              userVisibleOnly: true,
              applicationServerKey: self.urlBase64ToUint8Array(keyData.publicKey)
            });
          }

          var json = sub.toJSON();
          var payload = {
            endpoint: json.endpoint,
            keys: {
              p256dh: (json.keys && json.keys.p256dh) || '',
              auth: (json.keys && json.keys.auth) || ''
            }
          };
          if (!payload.endpoint || !payload.keys.p256dh || !payload.keys.auth) {
            return { ok: false, reason: 'incomplete-subscription' };
          }

          var subRes = await fetch('/Push/Subscribe', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
              'Content-Type': 'application/json',
              'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify(payload)
          });
          if (!subRes.ok) {
            console.warn('[ProfitNx push] Subscribe failed', subRes.status);
            return { ok: false, reason: 'subscribe-http-' + subRes.status };
          }
          window.__pnxPushSubscribed = true;
          console.info('[ProfitNx push] Subscribed OK');
          return { ok: true };
        } catch (err) {
          console.warn('[ProfitNx push] subscribe error', err);
          return { ok: false, reason: (err && err.message) || 'error' };
        } finally {
          self._pushBusy = null;
        }
      })();
      return self._pushBusy;
    },
    /** Call once on load: SW register + optional permission UI */
    boot: function () {
      var self = this;
      self.ensureSw();
      // After SW is ready, try Web Push subscription (needs permission)
      setTimeout(function () {
        if (Notification.permission === 'granted') self.subscribePush();
      }, 1500);
      setTimeout(function () {
        if (Notification.permission === 'granted' && !window.__pnxPushSubscribed) self.subscribePush();
      }, 8000);
      // Persistent enable bar if not granted
      function ensureBar() {
        if (!('Notification' in window)) return;
        if (Notification.permission === 'granted') {
          var old = document.getElementById('pnxNotifyEnableBar');
          if (old) old.remove();
          return;
        }
        if (document.getElementById('pnxNotifyEnableBar')) return;
        var bar = document.createElement('div');
        bar.id = 'pnxNotifyEnableBar';
        bar.setAttribute('role', 'status');
        bar.style.cssText = 'position:fixed;z-index:99999;left:12px;right:12px;bottom:12px;max-width:420px;margin:auto;background:#0f172a;color:#f8fafc;padding:12px 14px;border-radius:14px;box-shadow:0 12px 40px rgba(0,0,0,.35);display:flex;gap:10px;align-items:center;font:600 13px/1.35 system-ui,sans-serif;';
        bar.innerHTML = '<span style="flex:1">Desktop notifications enable karo — CRM minimize hoy tyare alert aavse.</span><button type="button" id="pnxNotifyEnableBtn" style="border:0;border-radius:10px;padding:8px 12px;background:linear-gradient(135deg,#7c3aed,#db2777);color:#fff;font-weight:700;cursor:pointer;">Enable</button><button type="button" id="pnxNotifyDismissBtn" style="border:0;background:transparent;color:#94a3b8;font-size:18px;cursor:pointer;line-height:1;" aria-label="Dismiss">×</button>';
        document.body.appendChild(bar);
        document.getElementById('pnxNotifyEnableBtn').onclick = function () {
          self.requestPermission().then(function (p) {
            if (p === 'granted') {
              bar.remove();
              self.subscribePush().then(function (r) {
                var extra = (r && r.ok) ? ' Web Push ON (tab bandh hoy tyare pan alert).' : '';
                self.show({
                  title: 'ProfitNx CRM',
                  body: 'Desktop notifications ON.' + extra,
                  url: location.pathname || '/',
                  tag: 'profitnx-notify-test',
                  force: true
                });
              });
            } else if (p === 'denied') {
              bar.querySelector('span').textContent = 'Browser ma Notifications blocked che. Site settings ma Allow karo.';
            }
          });
        };
        document.getElementById('pnxNotifyDismissBtn').onclick = function () { bar.remove(); };
      }
      if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', ensureBar);
      } else {
        ensureBar();
      }
      // Ask on first user gesture as well (Chrome requires gesture for permission prompt)
      var askOnce = function () {
        if (Notification.permission === 'default') {
          self.requestPermission().then(function (p) {
            if (p === 'granted') self.subscribePush();
          });
        } else if (Notification.permission === 'granted') {
          self.subscribePush();
        }
        document.removeEventListener('click', askOnce);
        document.removeEventListener('keydown', askOnce);
        document.removeEventListener('touchstart', askOnce);
      };
      document.addEventListener('click', askOnce, { once: true });
      document.addEventListener('keydown', askOnce, { once: true });
      document.addEventListener('touchstart', askOnce, { once: true, passive: true });
    }
  };
  try { window.ProfitNxDesktopNotify.boot(); } catch (_) {}



  const q = (selector, root = document) => root.querySelector(selector);
  const qa = (selector, root = document) => Array.from(root.querySelectorAll(selector));
  const isEditable = (element) => !!element && (element.matches('input, textarea, select, [contenteditable="true"]') || element.closest('[contenteditable="true"]'));
  const visible = (element) => !!element && element.getClientRects().length > 0 && !element.disabled;
  const normalizePath = (value) => {
    try {
      const url = new URL(value, location.origin);
      const search = new URLSearchParams(url.search);
      const ordered = Array.from(search.entries()).sort(([a], [b]) => a.localeCompare(b));
      return url.pathname.replace(/\/$/, '').toLowerCase() + (ordered.length ? '?' + new URLSearchParams(ordered).toString().toLowerCase() : '');
    } catch { return String(value || '').toLowerCase(); }
  };

  function initTooltips() {
    qa('button, a, input, select, textarea, [role="button"]').forEach((element) => {
      if (!element.getAttribute('title') && !element.dataset.bsTitle) {
        const text = (element.getAttribute('aria-label') || element.dataset.shortcutLabel || element.innerText || element.placeholder || element.name || '').trim().replace(/\s+/g, ' ');
        if (text && text.length <= 90) element.dataset.bsTitle = text;
      }
      if (element.dataset.bsTitle && !element.dataset.bsToggle) element.dataset.bsToggle = 'tooltip';
    });
    if (!window.bootstrap) return;
    qa('[data-bs-toggle="tooltip"]').forEach((element) => {
      if (!bootstrap.Tooltip.getInstance(element)) new bootstrap.Tooltip(element, { trigger: 'hover focus', boundary: document.body });
    });
  }

  function initDates() {
    if (window.flatpickr) flatpickr('.js-date', { dateFormat: 'Y-m-d', altInput: true, altFormat: 'd/m/Y', allowInput: true });
  }

  function initViewOnly() {
    qa('[data-view-only="True"], [data-view-only="true"]').forEach((container) => {
      qa('input, select, textarea, button[type="submit"]', container).forEach((control) => { control.disabled = true; });
    });
  }

  function initFilters() {
    qa('.js-clear-filter').forEach((button) => {
      button.addEventListener('click', () => {
        const form = button.closest('form');
        if (!form) return;
        qa('input, select', form).forEach((element) => { if (element.name && element.type !== 'hidden') element.value = ''; });
        form.submit();
      });
    });
  }

  function initPagination() {
    qa('table.js-paginated').forEach((table) => {
      const tbody = q('tbody', table);
      if (!tbody || table.dataset.pagerReady === 'true') return;
      const rows = Array.from(tbody.children).filter((row) => row.tagName === 'TR');
      if (rows.length <= 10) return;
      table.dataset.pagerReady = 'true';
      let page = 1;
      const pageSize = Number(table.dataset.pageSize || 10);
      const nav = document.createElement('div');
      nav.className = 'table-pager d-flex justify-content-between align-items-center flex-wrap gap-2 mt-3';
      nav.innerHTML = '<span class="mini-muted" data-page-info></span><div class="table-actions"><button type="button" class="btn btn-sm btn-outline-primary" data-page-prev>Previous</button><button type="button" class="btn btn-sm btn-outline-primary" data-page-next>Next</button></div>';
      table.parentElement.appendChild(nav);
      const info = q('[data-page-info]', nav);
      const previous = q('[data-page-prev]', nav);
      const next = q('[data-page-next]', nav);
      function draw() {
        const totalPages = Math.ceil(rows.length / pageSize);
        page = Math.min(Math.max(page, 1), totalPages);
        rows.forEach((row, index) => { row.style.display = index >= (page - 1) * pageSize && index < page * pageSize ? '' : 'none'; });
        info.textContent = `Showing ${Math.min((page - 1) * pageSize + 1, rows.length)}-${Math.min(page * pageSize, rows.length)} of ${rows.length}`;
        previous.disabled = page <= 1;
        next.disabled = page >= totalPages;
      }
      previous.addEventListener('click', () => { page -= 1; draw(); });
      next.addEventListener('click', () => { page += 1; draw(); });
      draw();
    });
  }

  function initMenuState() {
    const links = qa('.menu-link[href]').filter((link) => {
      const href = link.getAttribute('href') || '';
      return href && href !== '#' && !href.startsWith('javascript:');
    });
    const normalizePath = (url) => {
      try {
        const u = new URL(url, location.origin);
        let path = (u.pathname || '/').replace(/\/+$/, '') || '/';
        if (/\/index$/i.test(path)) path = path.replace(/\/index$/i, '') || '/';
        return path.toLowerCase();
      } catch {
        return String(url || '').toLowerCase();
      }
    };
    const currentPath = normalizePath(location.href);
    const currentQuery = (location.search || '').toLowerCase();
    let best = null;
    let bestScore = -1;
    links.forEach((link) => {
      const href = link.getAttribute('href') || link.href || '';
      let targetPath = normalizePath(href);
      let targetQuery = '';
      try { targetQuery = new URL(href, location.origin).search.toLowerCase(); } catch (e) { /* ignore */ }
      let score = -1;
      if (targetPath === currentPath && targetQuery === currentQuery) score = 100000 + targetPath.length;
      else if (targetPath === currentPath && !targetQuery) score = 80000 + targetPath.length;
      else if (targetPath === currentPath) score = 60000 + targetPath.length;
      // Only parent match if no other link shares a longer exact prefix later
      else if (targetPath !== '/' && currentPath.startsWith(targetPath + '/')) score = 1000 + targetPath.length;
      if (score > bestScore) {
        bestScore = score;
        best = link;
      }
      link.addEventListener('click', () => {
        try {
          localStorage.setItem('profitnx.activeMenu', targetPath + targetQuery);
        } catch (e) { /* ignore */ }
        // Optimistic UI: mark this item active immediately so it does not jump to top item
        links.forEach((l) => l.classList.remove('active'));
        link.classList.add('active');
        qa('.menu-group').forEach((group) => group.classList.toggle('has-active', !!q('.menu-link.active', group)));
      });
    });
    // If parent (/Dashboard) scored via startsWith but a child exact exists, child already wins with higher score.
    if (best) {
      links.forEach((link) => link.classList.toggle('active', link === best));
      try {
        localStorage.setItem('profitnx.activeMenu', normalizePath(best.href) + (new URL(best.href, location.origin).search.toLowerCase()));
      } catch (e) { /* ignore */ }
    }
    qa('.menu-group').forEach((group) => group.classList.toggle('has-active', !!q('.menu-link.active', group)));
    // Keep active item visible in sidebar without scrolling page to top
    const active = q('.menu-link.active');
    const sidebar = q('#crmSidebar') || q('.side-rail');
    if (active && sidebar && typeof active.scrollIntoView === 'function') {
      try { active.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (e) { /* ignore */ }
    }
  }

  function shortcutRows() {
    const rows = [];
    qa('[data-shortcut]').filter(visible).forEach((element) => {
      const label = (element.dataset.shortcutLabel || q('.menu-text', element)?.textContent || q('span', element)?.textContent || element.textContent || 'Open option').trim();
      rows.push({ key: element.dataset.shortcut, label, element });
    });
    rows.push({ key: 'Ctrl+S', label: 'Save current form' });
    rows.push({ key: '/', label: 'Focus page search' });
    rows.push({ key: '↑ / ↓', label: 'Move through table rows' });
    rows.push({ key: 'Enter', label: 'Open selected table row action' });
    return rows;
  }

  function renderShortcutList() {
    const host = q('[data-shortcut-list]');
    if (!host) return;
    host.replaceChildren();
    shortcutRows().forEach((row) => {
      const item = document.createElement('div');
      item.className = 'shortcut-item';
      item.innerHTML = `<kbd>${row.key}</kbd><span>${row.label}</span>`;
      host.appendChild(item);
    });
  }

  function showModal(id) {
    const element = document.getElementById(id);
    if (!element || !window.bootstrap) return;
    bootstrap.Modal.getOrCreateInstance(element).show();
  }

  function activateShortcut(key) {
    const match = qa('[data-shortcut]').find((element) => visible(element) && String(element.dataset.shortcut || '').toLowerCase() === key.toLowerCase());
    if (match) { match.click(); return true; }
    return false;
  }

  function submitCurrentForm() {
    const active = document.activeElement;
    const form = active?.closest('form') || qa('form').find((candidate) => visible(candidate) && q('button[type="submit"]', candidate));
    if (!form) return false;
    if (typeof form.requestSubmit === 'function') form.requestSubmit(); else form.submit();
    return true;
  }

  function focusSearch() {
    // Exclude the sidebar menu search (added 2026-08-02) so the existing
    // "/" shortcut still jumps to each page's own search/filter box, not
    // the nav search sitting earlier in the DOM.
    const target = qa('input[type="search"], input[name*="search" i], input[placeholder*="search" i], input[name="customer"], input[name="query"]')
      .filter((el) => el.id !== 'sidebarMenuSearch')
      .find(visible);
    if (!target) return false;
    target.focus(); target.select?.(); return true;
  }

  // ---- Change 2 (2026-08-02): left sidebar menu search ----
  // Filters the existing server-rendered menu links client-side as the user
  // types - no new endpoint, no page reload. A group (Workspace, Intelligence,
  // Administration, Business Masters) hides itself entirely once none of its
  // links match, and a small "no matches" note appears when nothing matches
  // at all.
  // Change 2 fix (2026-08-05): several older patch stylesheets set
  // `.menu-group{display:flex!important}` (final-ui-correction-2026-07-30.css,
  // premium-2026.css, professional-ui.css) with higher CSS specificity than
  // Bootstrap's `.d-none{display:none!important}`. Since both sides use
  // !important, the more specific selector always wins - so toggling the
  // `d-none` class on a group was silently being cancelled out and the menu
  // never actually filtered, even though the JS itself ran correctly. Using
  // an inline `style.setProperty(..., 'important')` instead sidesteps that
  // entirely: an inline !important always beats an external stylesheet
  // !important, regardless of any selector specificity, so this can no
  // longer be fought by any of the older CSS files.
  function setHidden(element, hidden) {
    if (!element) return;
    if (hidden) element.style.setProperty('display', 'none', 'important');
    else element.style.removeProperty('display');
  }

  function initSidebarMenuSearch() {
    const input = document.getElementById('sidebarMenuSearch');
    const clearBtn = document.getElementById('sidebarMenuSearchClear');
    const sidebar = document.getElementById('crmSidebar');
    const nav = sidebar ? q('.menu-list', sidebar) : null;
    if (!input || !nav) return;

    const groups = qa('.menu-group', nav);
    const emptyState = document.createElement('div');
    emptyState.className = 'sidebar-search-empty';
    emptyState.textContent = 'No matching menu items';
    setHidden(emptyState, true);
    nav.appendChild(emptyState);

    function applyFilter() {
      const term = input.value.trim().toLowerCase();
      setHidden(clearBtn, !term);
      let anyMatch = false;
      groups.forEach((group) => {
        let groupHasMatch = false;
        qa('.menu-link', group).forEach((link) => {
          const text = (q('.menu-text', link)?.textContent || link.textContent || '').trim().toLowerCase();
          const match = !term || text.includes(term);
          setHidden(link, !match);
          if (match) groupHasMatch = true;
        });
        setHidden(group, !groupHasMatch);
        if (groupHasMatch) anyMatch = true;
      });
      setHidden(emptyState, !term || anyMatch);
    }

    // 'input' covers typing/paste/IME in every modern browser; 'keyup' is
    // kept as a defensive fallback so filtering still works even if some
    // browser/extension swallows the input event.
    input.addEventListener('input', applyFilter);
    input.addEventListener('keyup', applyFilter);
    clearBtn?.addEventListener('click', () => {
      input.value = '';
      applyFilter();
      input.focus();
    });
    applyFilter();
  }

  function initKeyboard() {
    let selectedRow = null;
    document.addEventListener('keydown', (event) => {
      if (document.body.classList.contains('crm-locked') && event.target?.id !== 'crmLockPasswordInput') { return; }
      const key = event.key.toLowerCase();
      if (event.altKey && !event.ctrlKey && !event.metaKey) {
        const shortcut = key === '/' ? 'Alt+/' : `Alt+${key.toUpperCase()}`;
        if (shortcut === 'Alt+/' && q('[data-crm-shortcuts]')) { event.preventDefault(); renderShortcutList(); showModal('crmShortcutModal'); return; }
        if (shortcut === 'Alt+H' && q('[data-crm-help]')) { event.preventDefault(); openContextHelp(); return; }
        if (shortcut === 'Alt+Q' && q('[data-crm-lock-trigger]')) { event.preventDefault(); window.ProfitNxCRM.lockScreen?.(); return; }
        if (activateShortcut(shortcut)) { event.preventDefault(); return; }
      }
      if ((event.ctrlKey || event.metaKey) && key === 's') {
        if (submitCurrentForm()) event.preventDefault();
        return;
      }
      if (event.key === '/' && !event.ctrlKey && !event.metaKey && !event.altKey && !isEditable(event.target)) {
        if (focusSearch()) event.preventDefault();
        return;
      }
      if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && !isEditable(event.target)) {
        const rows = qa('table tbody tr').filter(visible);
        if (!rows.length) return;
        let index = selectedRow ? rows.indexOf(selectedRow) : -1;
        index = event.key === 'ArrowDown' ? Math.min(rows.length - 1, index + 1) : Math.max(0, index <= 0 ? 0 : index - 1);
        if (selectedRow) selectedRow.classList.remove('keyboard-row-active');
        selectedRow = rows[index];
        selectedRow.classList.add('keyboard-row-active');
        selectedRow.scrollIntoView({ block: 'nearest' });
        event.preventDefault();
        return;
      }
      if (event.key === 'Enter' && selectedRow && !isEditable(event.target)) {
        const action = q('a[href], button:not([disabled])', selectedRow);
        if (action) { action.click(); event.preventDefault(); }
      }
    });
    q('[data-crm-shortcuts]')?.addEventListener('click', () => { renderShortcutList(); showModal('crmShortcutModal'); });
  }

  const helpMap = [
    { match: /^\/dashboard/i, title: 'Dashboard Help', steps: ['Review KPI cards for today and this month.', 'Open Smart Focus items first.', 'Use the menu or keyboard shortcuts to continue.'] },
    { match: /^\/inquiry\/create/i, title: 'New Inquiry Help', steps: ['Enter customer and product details.', 'Choose the correct direct or partner flow.', 'Save the inquiry; required fields are validated automatically.'] },
    { match: /^\/inquiry/i, title: 'Inquiry Workspace Help', videoUrl: '', steps: [
        'Top KPI cards: tap Total / Follow Up / Demo Scheduled / Demo Done / Sold / Close (or New Inquiry / Genuine / Not Genuine for partner logins) to instantly filter the list by that status.',
        'Conversion Focus strip: Hot leads, Overdue follow-ups, Missing next-action date and Negotiation are shown here — clear these first every day.',
        'Send New Inquiry / New Inquiry button: use this to create a fresh customer inquiry; required fields are validated automatically before it saves.',
        'Filter panel: narrow the list by status, date range, quality, executive etc., then press Go. Use Clear to reset all filters.',
        'Status column: tap the status badge on any row to open its full Status Timeline history in a popup.',
        'Row actions: View opens read-only details, Edit lets you update the inquiry, Delete removes it (only shown if your login has that right).',
        'Quick Status form (inline in each row): change the status and press Save Status without leaving the list — set the Next Action Date here unless the status is Sold or Close.',
        'Forwarded inquiries: open them first and mark as attended, then continue the normal follow-up steps above.'
      ] },
    { match: /^\/reports/i, title: 'Reports Help', steps: ['Select a date range and required filters.', 'Use AI Insights for free rule-based recommendations.', 'Export detailed data only when your rights allow it.'] },
    { match: /^\/implementation/i, title: 'Implementation & Training Help', steps: ['Assign a support member and confirm customer date/time.', 'Create one schedule row per delivery day and record every covered point.', 'Complete training only after all pending points are handled; feedback is then emailed automatically.'] },
    { match: /^\/role\/userwiserights/i, title: 'User Wise Rights Help', steps: ['Enable Separate only for users needing exceptions.', 'Inherited users always follow Role Wise Rights.', 'Role changes do not overwrite Separate users.'] },
    { match: /^\/role\/permissions/i, title: 'Role Wise Rights Help', steps: ['Choose permissions for each role.', 'Save to apply them to all inherited users.', 'Separate users keep their own settings.'] },
    { match: /^\/product/i, title: 'Product Price Help', steps: ['Direct Customer Price is used for direct sales.', 'Partner Product Price is the partner base.', 'Partner margin is deducted automatically from the partner base price.'] },
    { match: /^\/stock/i, title: 'Stock Help', steps: ['Filter by partner, product, bill, or license.', 'Stock values use partner price minus partner margin.', 'Use Create Stock Bill only when your rights allow it.'] },
    { match: /^\/notifications/i, title: 'Notification Help', steps: ['Unread is tracked separately for every recipient.', 'Open an alert to mark only your copy as read.', 'Remarks and forwarding events also create notifications.'] },
    { match: /.*/, title: 'CRM Help', steps: ['Hover any control to see its tooltip.', 'Press Alt+/ for the rights-aware shortcut list.', 'Use the highlighted menu item to identify your current workspace.'] }
  ];

  function currentHelp() { return helpMap.find((item) => item.match.test(location.pathname)) || helpMap[helpMap.length - 1]; }

  function stopHelpVoice() {
    try {
      if (window.speechSynthesis) {
        window.speechSynthesis.cancel();
        // Some Chrome builds keep a residual utterance; double-cancel is safe
        window.setTimeout(function () {
          try { window.speechSynthesis.cancel(); } catch (e2) { /* ignore */ }
        }, 0);
      }
    } catch (e) { /* ignore */ }
  }

  // Change 2 (Batch 4): prefer an Indian English voice for the "Read Steps
  // Aloud" help narration. Falls back to the closest English voice, then
  // the browser default, since not every OS/browser ships an en-IN pack.
  let cachedVoices = [];
  function refreshVoiceCache() { if (window.speechSynthesis) cachedVoices = window.speechSynthesis.getVoices() || []; }
  if ('speechSynthesis' in window) {
    refreshVoiceCache();
    window.speechSynthesis.onvoiceschanged = () => {
      refreshVoiceCache();
      const i18n = window.ProfitNxHelpI18n;
      updateHelpVoiceNote(i18n ? i18n.getLang() : 'en');
    };
  }
  function pickHelpVoice(lang) {
    if (!cachedVoices.length) refreshVoiceCache();
    if (window.ProfitNxHelpI18n) return window.ProfitNxHelpI18n.pickVoice(cachedVoices, lang || 'en');
    const byLangExact = cachedVoices.find((v) => /en[-_]in/i.test(v.lang));
    if (byLangExact) return byLangExact;
    const anyEnglish = cachedVoices.find((v) => /^en/i.test(v.lang));
    return anyEnglish || null;
  }

  // Point 1 fix: even with the native-voice fallback above, most
  // Windows/desktop browsers (and many Android phones without the
  // Gujarati voice pack installed) simply have no real gu-IN voice, so the
  // narration quietly reads English instead — which reads as "the AI still
  // doesn't speak Gujarati properly". Rather than fail silently, show a
  // short on-screen note (in the same language) explaining that and how to
  // install a proper voice, so the person isn't left guessing why it
  // switched languages.
  function helpVoiceNoteText(/* lang */) {
    // User requested: no device/voice installation notes on the help modal.
    return '';
  }

  function updateHelpVoiceNote(/* lang */) {
    const note = q('[data-help-voice-note]');
    if (!note) return;
    note.textContent = '';
    note.style.display = 'none';
  }

  function hasTrueGujaratiVoice() {
    if (!cachedVoices.length) refreshVoiceCache();
    const i18n = window.ProfitNxHelpI18n;
    if (i18n && i18n.hasNativeGujaratiVoice) return i18n.hasNativeGujaratiVoice(cachedVoices);
    return cachedVoices.some(function (v) {
      return /^gu([-_]|$)/i.test(v.lang) || /gujarati/i.test(v.name);
    });
  }

  function setHelpVoiceBtnState(btn, ui, speaking) {
    if (!btn) return;
    const label = btn.querySelector('[data-help-voice-label]') || btn;
    const icon = btn.querySelector('i');
    if (speaking) {
      btn.classList.add('is-speaking');
      btn.setAttribute('aria-pressed', 'true');
      if (icon) icon.className = 'bi bi-stop-circle-fill';
      if (label && label !== btn) label.textContent = ui.stop || 'Stop Reading';
      else btn.innerHTML = '<i class="bi bi-stop-circle-fill"></i> <span data-help-voice-label>' + (ui.stop || 'Stop Reading') + '</span>';
    } else {
      btn.classList.remove('is-speaking');
      btn.setAttribute('aria-pressed', 'false');
      if (icon) icon.className = 'bi bi-volume-up-fill';
      if (label && label !== btn) label.textContent = ui.read || 'Read Steps Aloud';
      else btn.innerHTML = '<i class="bi bi-volume-up-fill"></i> <span data-help-voice-label>' + (ui.read || 'Read Steps Aloud') + '</span>';
    }
  }

  function wireHelpVoiceButton(help) {
    const btn = q('[data-help-voice-btn]');
    if (!btn) return;
    const i18n = window.ProfitNxHelpI18n;
    const lang = (help && help.lang) || (i18n ? i18n.getLang() : 'en');
    const ui = i18n ? i18n.uiText(lang) : { read: 'Read Steps Aloud', stop: 'Stop Reading', step: 'Step' };
    btn.onclick = null;
    if (!cachedVoices.length) refreshVoiceCache();

    // Gujarati: only enable voice when a real gu-IN / Gujarati voice exists.
    // Otherwise keep written Gujarati help only — no Hindi/English "fake" speech.
    const guOk = lang !== 'gu' || hasTrueGujaratiVoice();
    const speechOk = ('speechSynthesis' in window) && guOk;

    if (!speechOk) {
      // Hide Read Aloud for Gujarati when device has no proper Gujarati voice
      if (lang === 'gu') {
        btn.style.display = 'none';
        btn.setAttribute('disabled', 'disabled');
        btn.classList.add('disabled');
        btn.onclick = null;
        stopHelpVoice();
        return;
      }
      btn.style.display = '';
      btn.classList.add('disabled');
      btn.setAttribute('disabled', 'disabled');
      return;
    }

    btn.style.display = '';
    btn.removeAttribute('disabled');
    btn.classList.remove('disabled');
    setHelpVoiceBtnState(btn, ui, false);
    btn.onclick = () => {
      if (window.speechSynthesis.speaking || window.speechSynthesis.pending) {
        stopHelpVoice();
        clearStepHighlight();
        setHelpVoiceBtnState(btn, ui, false);
        return;
      }
      if (!cachedVoices.length) refreshVoiceCache();
      // Gujarati: only a true Gujarati voice — never Hindi/English fallback
      if (lang === 'gu' && !hasTrueGujaratiVoice()) {
        stopHelpVoice();
        setHelpVoiceBtnState(btn, ui, false);
        btn.style.display = 'none';
        return;
      }
      const speakHelp = help;
      const stepWord = ui.step || 'Step';
      let voice = pickHelpVoice(lang);
      if (lang === 'gu') {
        voice = cachedVoices.find(function (v) {
          return /^gu([-_]|$)/i.test(v.lang) || /gujarati/i.test(v.name);
        }) || null;
        if (!voice) {
          btn.style.display = 'none';
          return;
        }
      }
      const speechLangCode = (i18n && i18n.speechLang) ? i18n.speechLang(lang)
        : (lang === 'gu' ? 'gu-IN' : lang === 'hi' ? 'hi-IN' : 'en-IN');
      const rate = lang === 'gu' ? 0.86 : lang === 'hi' ? 0.88 : 0.95;
      const pitch = 1.0;

      const splitFn = (i18n && i18n.splitForSpeech)
        ? function (t) { return i18n.splitForSpeech(t, lang); }
        : function (t) { return [t]; };
      const prepFn = (i18n && i18n.prepareSpeechText)
        ? function (t) { return i18n.prepareSpeechText(t, lang); }
        : function (t) { return t; };

      const queue = [];
      splitFn(speakHelp.title).forEach(function (chunk) {
        queue.push({ text: prepFn(chunk), stepIndex: -1 });
      });
      (speakHelp.steps || []).forEach(function (step, index) {
        const labeled = stepWord + ' ' + (index + 1) + '. ' + step;
        splitFn(labeled).forEach(function (chunk) {
          queue.push({ text: prepFn(chunk), stepIndex: index });
        });
      });

      function speakNext() {
        const item = queue.shift();
        if (!item) {
          clearStepHighlight();
          setHelpVoiceBtnState(btn, ui, false);
          return;
        }
        highlightStep(item.stepIndex);
        const utterance = new SpeechSynthesisUtterance(item.text);
        utterance.lang = speechLangCode;
        if (voice) utterance.voice = voice;
        utterance.rate = rate;
        utterance.pitch = pitch;
        utterance.onend = function () {
          window.setTimeout(speakNext, (lang === 'gu' || lang === 'hi') ? 180 : 120);
        };
        utterance.onerror = function () { speakNext(); };
        try {
          window.speechSynthesis.speak(utterance);
        } catch (e) {
          speakNext();
        }
      }

      stopHelpVoice();
      window.setTimeout(function () {
        speakNext();
        setHelpVoiceBtnState(btn, ui, true);
      }, 50);
    };
  }

  function highlightStep(stepIndex) {
    clearStepHighlight();
    if (stepIndex < 0) return;
    const el = q(`[data-step-index="${stepIndex}"]`);
    if (el) { el.classList.add('help-step-active'); el.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); }
  }

  function clearStepHighlight() {
    document.querySelectorAll('.help-step-active').forEach((el) => el.classList.remove('help-step-active'));
  }

  function wireHelpLangSelect() {
    const sel = q('[data-help-lang]');
    if (!sel || !window.ProfitNxHelpI18n) return;
    sel.value = window.ProfitNxHelpI18n.getLang();
    sel.onchange = () => {
      stopHelpVoice();
      window.ProfitNxHelpI18n.setLang(sel.value);
      openContextHelp();
    };
  }

  function wireHelpVideoButton(help) {
    const el = q('[data-help-video-btn]');
    if (!el) return;
    const i18n = window.ProfitNxHelpI18n;
    const lang = (help && help.lang) || (i18n ? i18n.getLang() : 'en');
    const ui = i18n ? i18n.uiText(lang) : { video: 'Watch Video Guide', videoSoon: 'Video Guide (coming soon)' };
    const labelSpan = el.querySelector('[data-help-video-label]');
    if (help.videoUrl) {
      el.removeAttribute('disabled');
      el.removeAttribute('aria-disabled');
      el.classList.remove('disabled');
      if (el.tagName === 'A') el.href = help.videoUrl;
      if (labelSpan) labelSpan.textContent = ui.video || 'Watch Video Guide';
      else el.innerHTML = '<i class="bi bi-play-circle-fill"></i> <span data-help-video-label>' + (ui.video || 'Watch Video Guide') + '</span>';
      el.onclick = null;
    } else {
      el.setAttribute('disabled', 'disabled');
      el.setAttribute('aria-disabled', 'true');
      el.classList.add('disabled');
      if (el.tagName === 'A') el.href = '#';
      if (labelSpan) labelSpan.textContent = ui.videoSoon || 'Video Guide (coming soon)';
      else el.innerHTML = '<i class="bi bi-play-circle-fill"></i> <span data-help-video-label>' + (ui.videoSoon || 'Video Guide (coming soon)') + '</span>';
      el.onclick = (event) => { event.preventDefault(); event.stopPropagation(); };
    }
  }

  function openContextHelp() {
    let help = currentHelp();
    if (window.ProfitNxHelpI18n) help = window.ProfitNxHelpI18n.localize(help, window.ProfitNxHelpI18n.getLang());
    const title = q('[data-help-title]');
    const body = q('[data-help-body]');
    if (!title || !body) return;
    stopHelpVoice();
    const i18n = window.ProfitNxHelpI18n;
    const lang = help.lang || (i18n ? i18n.getLang() : 'en');
    const ui = i18n ? i18n.uiText(lang) : null;
    title.textContent = help.title;
    body.innerHTML = `<div class="help-steps">${help.steps.map((step, index) => `<div class="help-step" data-step-index="${index}"><b>${index + 1}</b><span>${step}</span></div>`).join('')}</div>`;
    const langLabel = q('[data-help-lang-label]');
    if (langLabel && ui) langLabel.textContent = ui.langLabel;
    const videoBtn = q('[data-help-video-btn]');
    if (videoBtn && ui) {
      // label refreshed in wireHelpVideoButton too
    }
    wireHelpLangSelect();
    wireHelpVoiceButton(help);
    wireHelpVideoButton(help);
    updateHelpVoiceNote(lang);
    if (videoBtn && ui) {
      const soon = videoBtn.getAttribute('aria-disabled') === 'true' || videoBtn.hasAttribute('disabled');
      const labelEl = videoBtn.querySelector('[data-help-video-label]');
      const iconHtml = soon ? '<i class="bi bi-play-circle-fill"></i> ' : '<i class="bi bi-play-circle-fill"></i> ';
      const labelText = soon ? (ui.videoSoon || 'Video Guide (coming soon)') : (ui.video || 'Watch Video Guide');
      if (labelEl) {
        labelEl.textContent = labelText;
      } else {
        videoBtn.innerHTML = iconHtml + '<span data-help-video-label>' + labelText + '</span>';
      }
    }
    showModal('crmHelpModal');
  }

  function stopHelpVoiceAndUi() {
    stopHelpVoice();
    clearStepHighlight();
    const btn = q('[data-help-voice-btn]');
    if (btn) {
      const i18n = window.ProfitNxHelpI18n;
      const lang = i18n ? i18n.getLang() : 'en';
      const ui = i18n ? i18n.uiText(lang) : { read: 'Read Steps Aloud', stop: 'Stop Reading' };
      setHelpVoiceBtnState(btn, ui, false);
    }
  }

  function initHelp() {
    const button = q('[data-crm-help]');
    if (!button) return;
    button.addEventListener('click', openContextHelp);

    // Always stop speech when help modal is closing or closed (X, Esc, backdrop)
    const helpModal = q('#crmHelpModal');
    if (helpModal && !helpModal.dataset.helpVoiceBound) {
      helpModal.dataset.helpVoiceBound = '1';
      helpModal.addEventListener('hide.bs.modal', stopHelpVoiceAndUi);
      helpModal.addEventListener('hidden.bs.modal', stopHelpVoiceAndUi);
    }
    const key = `profitnx.helpSeen:${location.pathname.toLowerCase()}`;
    if (!sessionStorage.getItem(key)) {
      sessionStorage.setItem(key, '1');
      let help = currentHelp();
      if (window.ProfitNxHelpI18n) help = window.ProfitNxHelpI18n.localize(help, window.ProfitNxHelpI18n.getLang());
      const showLabel = window.ProfitNxHelpI18n ? window.ProfitNxHelpI18n.uiText(help.lang || 'en').showSteps : 'Show steps';
      const nudge = document.createElement('div');
      nudge.className = 'crm-help-nudge';
      nudge.innerHTML = `<button type="button" class="crm-nudge-close" aria-label="Close">×</button><i class="bi bi-lightbulb-fill"></i><div><b>${help.title}</b><span>${help.steps[0] || ''}</span><button type="button" class="btn btn-sm btn-light mt-2" data-open-help>${showLabel}</button></div>`;
      document.body.appendChild(nudge);
      q('[data-open-help]', nudge).addEventListener('click', () => { nudge.remove(); openContextHelp(); });
      q('.crm-nudge-close', nudge).addEventListener('click', () => nudge.remove());
      window.setTimeout(() => nudge.classList.add('show'), 700);
      window.setTimeout(() => nudge.remove(), 12000);
    }
  }

  function elapsedText(minutes) {
    const value = Number(minutes || 0);
    if (value < 60) return `${value} min`;
    if (value < 1440) return `${Math.floor(value / 60)} hr ${value % 60} min`;
    return `${Math.floor(value / 1440)} day ${Math.floor((value % 1440) / 60)} hr`;
  }

  function renderAttention(items) {
    const host = q('[data-attention-list]');
    if (!host) return;
    host.replaceChildren();
    const theme = (document.documentElement.getAttribute('data-app-theme') || '').toLowerCase();
    const isDark = theme === 'midnight' || theme === 'premium';
    items.forEach((item) => {
      const row = document.createElement('a');
      row.className = 'attention-item';
      row.href = item.url || '#';
      const mobileHtml = item.mobile
        ? `<a class="attention-call" href="tel:${escapeHtml(item.mobile)}" title="Call ${escapeHtml(item.mobile)}" onclick="event.stopPropagation()"><i class="bi bi-telephone-fill"></i> ${escapeHtml(item.mobile)}</a>`
        : '';
      const name = (item.name || '').trim();
      const firm = (item.firm || '').trim();
      const city = (item.city || '').trim();
      const title = firm || name || item.customer || 'Inquiry';
      const metaParts = [];
      if (name && firm && name !== firm) metaParts.push(name);
      if (city) metaParts.push(city);
      const metaLine = metaParts.length ? `<span class="attention-meta">${escapeHtml(metaParts.join(' · '))}</span>` : '';
      const lastUpd = item.lastUpdatedText
        ? `<span class="attention-last">Last update: ${escapeHtml(item.lastUpdatedText)}</span>`
        : '';
      row.innerHTML = `<div class="attention-icon"><i class="bi bi-exclamation-triangle-fill"></i></div><div class="attention-body"><b class="attention-title">${escapeHtml(title)}</b>${metaLine}<span class="attention-status">${escapeHtml(item.status || '')} · ${escapeHtml(item.reason || 'Action pending')}</span>${mobileHtml}${lastUpd}<small class="attention-pending">Pending for ${elapsedText(item.elapsedMinutes)}</small></div><i class="bi bi-arrow-right-circle attention-arrow"></i>`;
      // Guaranteed contrast for Midnight / Premium Elite (inline beats any leftover CSS)
      if (isDark) {
        const isPremium = theme === 'premium';
        row.style.setProperty('background', isPremium
          ? 'linear-gradient(135deg, #1f1738 0%, #120c24 100%)'
          : 'linear-gradient(135deg, #1e293b 0%, #0f172a 100%)', 'important');
        row.style.setProperty('border', isPremium
          ? '1px solid rgba(202,161,74,0.4)'
          : '1px solid #475569', 'important');
        row.style.setProperty('color', '#f1f5f9', 'important');
        const titleEl = row.querySelector('.attention-title');
        if (titleEl) titleEl.style.setProperty('color', isPremium ? '#fff8e8' : '#ffffff', 'important');
        row.querySelectorAll('.attention-meta, .attention-status').forEach((el) => {
          el.style.setProperty('color', isPremium ? '#d4c4f0' : '#cbd5e1', 'important');
          el.style.setProperty('opacity', '1', 'important');
        });
        const lastEl = row.querySelector('.attention-last');
        if (lastEl) {
          lastEl.style.setProperty('color', isPremium ? '#c4b5e0' : '#94a3b8', 'important');
          lastEl.style.setProperty('opacity', '1', 'important');
        }
        const pendEl = row.querySelector('.attention-pending');
        if (pendEl) {
          pendEl.style.setProperty('color', '#fbbf24', 'important');
          pendEl.style.setProperty('opacity', '1', 'important');
          pendEl.style.setProperty('font-weight', '800', 'important');
        }
        const iconEl = row.querySelector('.attention-icon');
        if (iconEl) {
          iconEl.style.setProperty('background', isPremium ? 'rgba(202,161,74,0.2)' : 'rgba(251,191,36,0.18)', 'important');
          iconEl.style.setProperty('color', isPremium ? '#caa14a' : '#fbbf24', 'important');
        }
        const arrowEl = row.querySelector('.attention-arrow');
        if (arrowEl) arrowEl.style.setProperty('color', isPremium ? '#c4b5e0' : '#94a3b8', 'important');
        const callEl = row.querySelector('.attention-call');
        if (callEl) {
          callEl.style.setProperty('background', '#fde68a', 'important');
          callEl.style.setProperty('color', '#0f172a', 'important');
          callEl.style.setProperty('border', '1px solid #fbbf24', 'important');
          callEl.style.setProperty('font-weight', '800', 'important');
        }
      }
      host.appendChild(row);
    });
  }

  function notifyAttentionIfMinimized(items) {
    if (!items || !items.length) return;
    if (!window.ProfitNxDesktopNotify) return;
    var first = items[0] || {};
    var title = 'ProfitNx CRM — Pending Inquiry';
    var body = items.length === 1
      ? ((first.customer || first.firm || first.name || 'Inquiry') + ' · ' + (first.reason || 'Action pending') + (first.mobile ? ' · ' + first.mobile : ''))
      : (items.length + ' inquiries need attention — forwarded or overdue follow-ups.');
    var url = first.url || '/Inquiry';
    var tag = 'profitnx-attention-' + (first.id || 'batch');
    // force when background; when focused, still notify if user wants OS alerts
    // User requirement: desktop notification must appear when minimized.
    window.ProfitNxDesktopNotify.show({
      title: title,
      body: body,
      url: url,
      tag: tag,
      force: window.ProfitNxDesktopNotify.isBackground()
    });
  }

  function initAttention() {
    const shell = q('[data-crm-shell]');
    if (!shell || shell.dataset.attentionEnabled !== 'true' || !shell.dataset.attentionEndpoint || !q('#crmAttentionModal')) return;
    let lastSignature = '';
    // Request permission early (user gesture + also on first load if already decided)
    if ('Notification' in window) {
      if (Notification.permission === 'default') {
        const ask = () => {
          Notification.requestPermission().catch(() => {});
          document.removeEventListener('click', ask);
          document.removeEventListener('keydown', ask);
          document.removeEventListener('touchstart', ask);
        };
        document.addEventListener('click', ask, { once: true });
        document.addEventListener('keydown', ask, { once: true });
        document.addEventListener('touchstart', ask, { once: true, passive: true });
      }
    }
    // Ensure SW is registered so showNotification works while minimized
    if ('serviceWorker' in navigator) {
      navigator.serviceWorker.register('/service-worker.js').catch(() => {});
    }

    async function checkAttention(force = false) {
      try {
        const response = await fetch(shell.dataset.attentionEndpoint, { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        if (!response.ok) return;
        const data = await response.json();
        const items = Array.isArray(data.items) ? data.items : [];
        if (!items.length) {
          lastSignature = '';
          return;
        }
        // Signature is id-only so we re-fire when the set of pending items changes
        const signature = items.map((item) => item.id).join('|');
        const lastShown = Number(sessionStorage.getItem('profitnx.attentionLastShown') || 0);
        const lastDesktop = Number(sessionStorage.getItem('profitnx.attentionLastDesktop') || 0);
        const isBackground = document.hidden || document.visibilityState === 'hidden' || !document.hasFocus();

        if (force || signature !== lastSignature || Date.now() - lastShown > 180000) {
          lastSignature = signature;
          renderAttention(items);
          sessionStorage.setItem('profitnx.attentionLastShown', String(Date.now()));
          if (!isBackground) {
            showModal('crmAttentionModal');
          }
          notifyAttentionIfMinimized(items);
          sessionStorage.setItem('profitnx.attentionLastDesktop', String(Date.now()));
        } else if (isBackground && Date.now() - lastDesktop > 45000) {
          // Re-notify every 90 seconds while still pending and window minimized
          notifyAttentionIfMinimized(items);
          sessionStorage.setItem('profitnx.attentionLastDesktop', String(Date.now()));
        }
      } catch {}
    }

    window.setTimeout(() => checkAttention(true), 1200);
    // Foreground poll
    window.setInterval(() => checkAttention(false), 30000);
    // Aggressive background poll (browsers still throttle, but shorter interval helps)
    let bgTimer = null;
    function startBgPoll() {
      if (bgTimer) return;
      bgTimer = setInterval(() => {
        if (document.hidden || document.visibilityState === 'hidden' || !document.hasFocus()) {
          checkAttention(false);
        }
      }, 8000);
    }
    function stopBgPoll() {
      if (bgTimer) { clearInterval(bgTimer); bgTimer = null; }
    }
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        startBgPoll();
        checkAttention(true);
      } else {
        stopBgPoll();
        checkAttention(true);
      }
    });
    window.addEventListener('blur', () => {
      startBgPoll();
      setTimeout(() => checkAttention(true), 250);
    });
    window.addEventListener('focus', () => {
      stopBgPoll();
      checkAttention(true);
    });
    window.addEventListener('pagehide', () => { checkAttention(true); });
    document.addEventListener('pnx-force-poll', () => { checkAttention(true); });
    // If already backgrounded at init time
    if (document.hidden || document.visibilityState === 'hidden') startBgPoll();
  }

  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>'"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[char]));
  }

  function renderImplementationReminders(items) {
    const host = q('[data-implementation-reminder-list]');
    if (!host) return;
    host.replaceChildren();
    items.forEach((item) => {
      const link = document.createElement('a');
      link.className = 'implementation-reminder-item';
      link.href = item.url || '#';
      link.innerHTML = `<div class="reminder-clock"><i class="bi bi-alarm-fill"></i></div><div><strong>${escapeHtml(item.customer || 'Customer')}</strong><span>Day ${escapeHtml(item.day || '')} · ${escapeHtml(item.stage || 'Training')} · ${escapeHtml(item.time || '')}</span><small>License: ${escapeHtml(item.license || 'N/A')} · ${escapeHtml(item.topic || 'Review schedule')}</small></div><i class="bi bi-arrow-right"></i>`;
      host.appendChild(link);
    });
  }

  function initImplementationReminders() {
    const shell = q('[data-crm-shell]');
    if (!shell || shell.dataset.implementationReminderEnabled !== 'true' || !shell.dataset.implementationReminderEndpoint || !q('#implementationReminderModal')) return;
    let lastSignature = '';
    async function check() {
      try {
        const response = await fetch(shell.dataset.implementationReminderEndpoint, { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        if (!response.ok) return;
        const data = await response.json();
        const items = Array.isArray(data.items) ? data.items : [];
        if (!items.length) return;
        const signature = items.map((item) => item.id).join('|');
        const sessionKey = `profitnx.implementationReminder:${signature}`;
        if (signature !== lastSignature && !sessionStorage.getItem(sessionKey)) {
          lastSignature = signature;
          sessionStorage.setItem(sessionKey, '1');
          renderImplementationReminders(items);
          showModal('implementationReminderModal');
        }
      } catch {}
    }
    window.setTimeout(check, 1200);
    window.setInterval(check, 30000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) check(); });
  }

  function initAchievement() {
    const shell = q('[data-crm-shell]');
    if (!shell || !shell.dataset.achievementMessage || !q('#crmAchievementModal')) return;
    window.setTimeout(() => showModal('crmAchievementModal'), 500);
  }

  function initMobileNavigation() {
    const toggle = q('[data-sidebar-toggle]');
    const backdrop = q('[data-sidebar-backdrop]');
    const sidebar = q('#crmSidebar');
    if (!toggle || !sidebar) return;

    const setOpen = (open) => {
      document.body.classList.toggle('crm-nav-open', open);
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
      backdrop?.setAttribute('aria-hidden', open ? 'false' : 'true');
    };

    toggle.addEventListener('click', () => setOpen(!document.body.classList.contains('crm-nav-open')));
    backdrop?.addEventListener('click', () => setOpen(false));
    qa('a.menu-link', sidebar).forEach((link) => link.addEventListener('click', () => setOpen(false)));
    document.addEventListener('keydown', (event) => { if (event.key === 'Escape') setOpen(false); });
    window.addEventListener('resize', () => { if (window.innerWidth > 900) setOpen(false); });
  }

  window.ProfitNxCRM.confirmImplementationSetup = function (details = {}) {
    const element = q('#implementationSetupConfirmModal');
    if (!element || !window.bootstrap) return Promise.resolve(false);

    const customer = q('[data-implementation-customer]', element);
    const product = q('[data-implementation-product]', element);
    if (customer) customer.textContent = details.customer || 'Sold inquiry customer';
    if (product) product.textContent = details.product || 'Selected product';

    return new Promise((resolve) => {
      const modal = bootstrap.Modal.getOrCreateInstance(element, { backdrop: 'static', keyboard: true });
      const yes = q('[data-implementation-choice="yes"]', element);
      const no = q('[data-implementation-choice="no"]', element);
      let settled = false;

      const finish = (value) => {
        if (settled) return;
        settled = true;
        cleanup();
        modal.hide();
        resolve(value);
      };
      const cancelled = () => {
        if (settled) return;
        settled = true;
        cleanup();
        resolve(null);
      };
      const cleanup = () => {
        yes?.removeEventListener('click', chooseYes);
        no?.removeEventListener('click', chooseNo);
        element.removeEventListener('hidden.bs.modal', cancelled);
      };
      const chooseYes = () => finish(true);
      const chooseNo = () => finish(false);

      yes?.addEventListener('click', chooseYes);
      no?.addEventListener('click', chooseNo);
      element.addEventListener('hidden.bs.modal', cancelled, { once: true });
      modal.show();
    });
  };

  // REMOVED (2026-08-18): confirmTrainingPaidOrFree() / the paid-or-free
  // confirm popup it drove has been removed per request - "Paid training" is
  // a plain optional switch again, selected by the person themselves.

  function initPwaInstall() {
    const buttons = qa('[data-install-app]');
    const modalElement = q('#pwaInstallModal');
    const installNow = q('[data-pwa-install-now]', modalElement || document);
    const message = q('[data-pwa-install-message]', modalElement || document);
    const steps = q('[data-pwa-install-steps]', modalElement || document);
    let deferredPrompt = null;

    const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent);
    const isStandalone = window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;

    function renderInstallGuidance() {
      if (!modalElement || !window.bootstrap) return;
      if (isStandalone) {
        if (message) message.textContent = 'ProfitNx CRM is already installed on this device.';
        if (steps) steps.innerHTML = '<div><b>✓</b><span>Open it from your home screen or app launcher.</span></div>';
        if (installNow) { installNow.innerHTML = '<i class="bi bi-check2-circle"></i> Installed'; installNow.disabled = true; }
      } else if (deferredPrompt) {
        if (message) message.textContent = 'Your device is ready to install ProfitNx CRM as an app.';
        if (steps) steps.innerHTML = '<div><b>1</b><span>Tap Install App below.</span></div><div><b>2</b><span>Confirm the browser installation prompt.</span></div><div><b>3</b><span>Open CRM from the new app icon.</span></div>';
        if (installNow) { installNow.innerHTML = '<i class="bi bi-download"></i> Install App'; installNow.disabled = false; }
      } else if (isIos) {
        if (message) message.textContent = 'On iPhone or iPad, install ProfitNx CRM from Safari.';
        if (steps) steps.innerHTML = '<div><b>1</b><span>Open this page in Safari.</span></div><div><b>2</b><span>Tap Share, then Add to Home Screen.</span></div><div><b>3</b><span>Tap Add to install the CRM icon.</span></div>';
        if (installNow) { installNow.innerHTML = '<i class="bi bi-check2"></i> Got It'; installNow.disabled = false; }
      } else {
        if (message) message.textContent = 'Open the browser menu and choose Install app or Add to Home Screen.';
        if (steps) steps.innerHTML = '<div><b>1</b><span>Open this CRM in Chrome or Edge.</span></div><div><b>2</b><span>Choose Install app / Add to Home Screen.</span></div><div><b>3</b><span>Launch CRM from the app icon.</span></div>';
        if (installNow) { installNow.innerHTML = '<i class="bi bi-check2"></i> Got It'; installNow.disabled = false; }
      }
      bootstrap.Modal.getOrCreateInstance(modalElement).show();
    }

    window.addEventListener('beforeinstallprompt', (event) => {
      event.preventDefault();
      deferredPrompt = event;
      buttons.forEach((button) => button.classList.add('install-ready'));
    });

    window.addEventListener('appinstalled', () => {
      deferredPrompt = null;
      buttons.forEach((button) => button.classList.remove('install-ready'));
    });

    buttons.forEach((button) => button.addEventListener('click', renderInstallGuidance));
    installNow?.addEventListener('click', async () => {
      if (deferredPrompt) {
        deferredPrompt.prompt();
        await deferredPrompt.userChoice;
        deferredPrompt = null;
        buttons.forEach((button) => button.classList.remove('install-ready'));
      }
      if (modalElement && window.bootstrap) bootstrap.Modal.getInstance(modalElement)?.hide();
    });

    if ('serviceWorker' in navigator && (location.protocol === 'https:' || location.hostname === 'localhost')) {
      const hadController = Boolean(navigator.serviceWorker.controller);
      navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (!hadController || sessionStorage.getItem('profitnx-sw-reloaded') === '1') return;
        sessionStorage.setItem('profitnx-sw-reloaded', '1');
        window.location.reload();
      });
      navigator.serviceWorker.register('/service-worker.js').then((registration) => {
        registration.update().catch(() => {});
        window.setInterval(() => registration.update().catch(() => {}), 60 * 60 * 1000);
      }).catch(() => {});
    }
  }

  function initGlobalConfirmations() {
    qa('form').forEach((form) => {
      if (form.dataset.confirm) form.addEventListener('submit', (event) => { if (!confirm(form.dataset.confirm)) event.preventDefault(); });
    });
  }

  function initLockScreen() {
    const overlay = document.getElementById('crmLockOverlay');
    if (!overlay) return;
    const trigger = q('[data-crm-lock-trigger]');
    const form = document.getElementById('crmLockUnlockForm');
    const input = document.getElementById('crmLockPasswordInput');
    const toggle = document.getElementById('crmLockPasswordToggle');
    const error = document.getElementById('crmLockError');
    const card = q('.crm-lock-card', overlay);
    const submitBtn = document.getElementById('crmLockUnlockBtn');
    const LOCK_KEY = 'profitnx.locked';

    function showLock() {
      overlay.classList.add('show');
      document.body.classList.add('crm-locked');
      overlay.setAttribute('aria-hidden', 'false');
      sessionStorage.setItem(LOCK_KEY, '1');
      window.setTimeout(() => input?.focus(), 50);
    }
    function hideLock() {
      overlay.classList.remove('show');
      document.body.classList.remove('crm-locked');
      overlay.setAttribute('aria-hidden', 'true');
      sessionStorage.removeItem(LOCK_KEY);
      if (input) input.value = '';
      error?.classList.add('d-none');
    }

    trigger?.addEventListener('click', showLock);

    toggle?.addEventListener('click', () => {
      if (!input) return;
      const showing = input.type === 'text';
      input.type = showing ? 'password' : 'text';
      toggle.innerHTML = showing ? '<i class="bi bi-eye"></i>' : '<i class="bi bi-eye-slash"></i>';
    });

    form?.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!input || !input.value) return;
      const tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
      if (submitBtn) { submitBtn.disabled = true; submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Checking...'; }
      try {
        const body = new URLSearchParams();
        body.set('password', input.value);
        if (tokenInput) body.set('__RequestVerificationToken', tokenInput.value);
        const response = await fetch(form.getAttribute('action') || form.action, {
          method: 'POST',
          headers: { 'X-Requested-With': 'XMLHttpRequest', 'Content-Type': 'application/x-www-form-urlencoded' },
          body: body.toString()
        });
        const data = await response.json().catch(() => ({ ok: false }));
        if (data && data.ok) {
          hideLock();
        } else {
          error?.classList.remove('d-none');
          card?.classList.add('shake');
          window.setTimeout(() => card?.classList.remove('shake'), 400);
          input.value = '';
          input.focus();
        }
      } catch {
        error?.classList.remove('d-none');
      } finally {
        if (submitBtn) { submitBtn.disabled = false; submitBtn.innerHTML = '<i class="bi bi-unlock-fill"></i> Unlock'; }
      }
    });

    // Keeps the CRM locked across page reloads / navigation within this browser tab
    // (matches "system chalu hoi pan CRM lock rahe" — until the right password unlocks it).
    if (sessionStorage.getItem(LOCK_KEY) === '1') showLock();

    window.ProfitNxCRM.lockScreen = showLock;
  }

  // Change 2 (2026-08-05): each init used to run back-to-back in one block,
  // so if any single one threw (e.g. a missing element on a particular
  // page), every init still to come - including initSidebarMenuSearch -
  // silently never ran at all, which is exactly why the sidebar search box
  // could show up but typing into it did nothing. Each init now runs in its
  // own try/catch so one failure can never block the rest.
  function safeInit(name, fn) {
    try { fn(); } catch (err) { if (window.console) console.error('[ProfitNxCRM] ' + name + ' failed:', err); }
  }

  document.addEventListener('DOMContentLoaded', function () {
    safeInit('initLockScreen', initLockScreen);
    safeInit('initSidebarMenuSearch', initSidebarMenuSearch);
    safeInit('initTooltips', initTooltips);
    safeInit('initDates', initDates);
    safeInit('initMobileNavigation', initMobileNavigation);
    safeInit('initPwaInstall', initPwaInstall);
    safeInit('initViewOnly', initViewOnly);
    safeInit('initFilters', initFilters);
    safeInit('initPagination', initPagination);
    safeInit('initMenuState', initMenuState);
    safeInit('renderShortcutList', renderShortcutList);
    safeInit('initKeyboard', initKeyboard);
    safeInit('initHelp', initHelp);
    safeInit('initAttention', initAttention);
    safeInit('initImplementationReminders', initImplementationReminders);
    safeInit('initAchievement', initAchievement);
    safeInit('initGlobalConfirmations', initGlobalConfirmations);
  });
})();
