/**
 * ProfitNx CRM — Enterprise UI behaviors (2026-09-08 + pagination/filters pass)
 * - Gmail-style collapsible sidebar (persisted)
 * - Background-safe notification helpers (visibility + SW)
 * - Client table helpers: pagination (page size + prev/next + page numbers),
 *   column-wise filters under headers, sticky Action reflow
 */
(function () {
  'use strict';

  var SIDEBAR_KEY = 'profitnx.sidebar.collapsed';

  function q(sel, root) { return (root || document).querySelector(sel); }
  function qa(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }

  /* ---------- Collapsible sidebar ---------- */
  function initSidebarCollapse() {
    if (!document.body.classList.contains('crm-app-body')) return;
    var toggle = q('#sidebarCollapseToggle');
    var sidebar = q('#crmSidebar');
    if (!toggle || !sidebar) return;

    function isDesktop() {
      return window.matchMedia('(min-width: 992px)').matches;
    }

    function applyCollapsed(collapsed) {
      if (!isDesktop()) {
        document.body.classList.remove('sidebar-collapsed');
        toggle.setAttribute('aria-expanded', 'true');
        toggle.setAttribute('aria-label', 'Collapse sidebar');
        if (toggle.getAttribute('data-bs-title') !== null) {
          toggle.setAttribute('data-bs-title', 'Collapse sidebar');
        }
        return;
      }
      document.body.classList.toggle('sidebar-collapsed', !!collapsed);
      toggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
      toggle.setAttribute('aria-label', collapsed ? 'Expand sidebar' : 'Collapse sidebar');
      toggle.setAttribute('data-bs-title', collapsed ? 'Expand sidebar' : 'Collapse sidebar');
      try {
        if (window.bootstrap && bootstrap.Tooltip) {
          var inst = bootstrap.Tooltip.getInstance(toggle);
          if (inst) inst.setContent({ '.tooltip-inner': collapsed ? 'Expand sidebar' : 'Collapse sidebar' });
        }
      } catch (_) { /* ignore */ }
    }

    var stored = false;
    try { stored = localStorage.getItem(SIDEBAR_KEY) === '1'; } catch (_) { /* ignore */ }
    applyCollapsed(stored);

    toggle.addEventListener('click', function () {
      if (!isDesktop()) return;
      var next = !document.body.classList.contains('sidebar-collapsed');
      applyCollapsed(next);
      try { localStorage.setItem(SIDEBAR_KEY, next ? '1' : '0'); } catch (_) { /* ignore */ }
    });

    window.addEventListener('resize', function () {
      var storedNow = false;
      try { storedNow = localStorage.getItem(SIDEBAR_KEY) === '1'; } catch (_) { /* ignore */ }
      applyCollapsed(storedNow);
    });

    document.addEventListener('keydown', function (e) {
      if (e.altKey && (e.key === '[' || e.code === 'BracketLeft') && isDesktop()) {
        e.preventDefault();
        toggle.click();
      }
    });
  }

  /* ---------- Background notification reliability ---------- */
  function enhanceBackgroundNotifications() {
    if (!('Notification' in window)) return;

    if ('serviceWorker' in navigator) {
      navigator.serviceWorker.register('/service-worker.js').catch(function () { /* ignore */ });
    }

    document.addEventListener('visibilitychange', function () {
      if (document.visibilityState === 'hidden') {
        document.dispatchEvent(new CustomEvent('pnx-force-poll'));
      }
    });

    if (Notification.permission === 'default') {
      var asked = false;
      function askOnce() {
        if (asked) return;
        asked = true;
        try { Notification.requestPermission(); } catch (_) { /* ignore */ }
        document.removeEventListener('click', askOnce);
        document.removeEventListener('keydown', askOnce);
      }
      document.addEventListener('click', askOnce, { once: true });
      document.addEventListener('keydown', askOnce, { once: true });
    }
  }

  /* ---------- Sticky Action column reflow ---------- */
  // Hard pixel-lock: every header/body cell in the Action column gets the
  // SAME width + overflow:hidden, regardless of which row markup rendered
  // it (Support/SupportHead vs Admin/User rows use different inner
  // wrappers). Without this, the column's auto-computed width could differ
  // slightly per row content, which is what made the freeze look like it
  // was "scrolling" or getting filled over by adjacent columns.
  // NOTE (2026-09-10): width used to be hardcoded here (128px) AND separately
  // hardcoded in action-column-freeze-fix-2026-09-10.css (114px), both applied
  // with `!important`. Inline `!important` styles set from JS always beat an
  // external stylesheet's `!important`, so this JS value silently won every
  // time — but only right after it ran. Since this function re-runs on every
  // scroll tick, resize, and DOM mutation, the box would flicker between the
  // two widths depending on which one last applied, which is what showed up
  // as the frozen Action column "un-freezing" / jumping during a scroll drag.
  // Fix: JS no longer sets width/overflow at all — the CSS file is now the
  // single source of truth for sizing. JS only (re)asserts position/right/
  // z-index, which is genuinely needed because some rows are re-rendered by
  // the pagination/filter helpers without carrying sticky positioning along.
  function forceStickyActionReflow() {
    qa('table.inquiry-action-table, table.enterprise-data-table, table.crm-data-table, table.pg-table').forEach(function (table) {
      var cells = qa('th.col-action, td.col-action, td.inquiry-action-td', table);
      if (!cells.length) {
        // Fallback: last column when marked as action table
        if (table.classList.contains('inquiry-action-table')) {
          cells = qa('thead th:last-child, tbody td:last-child', table);
        }
      }
      cells.forEach(function (cell) {
        cell.style.setProperty('position', 'sticky', 'important');
        cell.style.setProperty('right', '0', 'important');
        cell.style.setProperty('z-index', '50', 'important');
        void cell.offsetHeight;
      });
    });
  }

  /* ---------- Lightweight client-side table helpers ---------- */
  /**
   * Apply to any table with class "enterprise-data-table" (and optionally data-table-id).
   * Also auto-enhances tables.pg-table / .report-table when they have data-table-id.
   * Supports: page size, global search, per-column filters, column show/hide, page numbers.
   */
  function initEnterpriseTables() {
    var targets = qa('table.enterprise-data-table, table.pg-table[data-table-id], table.report-table[data-table-id]');
    targets.forEach(function (table) {
      if (table.getAttribute('data-enterprise-init') === '1') return;
      table.setAttribute('data-enterprise-init', '1');

      var tableId = table.getAttribute('data-table-id') || table.id || ('tbl_' + Math.random().toString(36).slice(2, 8));
      if (!table.getAttribute('data-table-id')) table.setAttribute('data-table-id', tableId);
      if (!table.classList.contains('enterprise-data-table')) table.classList.add('enterprise-data-table');

      var userKey = (window.__pnxUserId || 'anon') + '.' + tableId;
      var storageKey = 'profitnx.tableLayout.' + userKey;
      var tbody = table.tBodies[0];
      if (!tbody) return;

      var rows = qa('tr', tbody).filter(function (r) {
        return !r.classList.contains('enterprise-empty-row') && !r.classList.contains('filter-row');
      });
      var useVirtual = table.classList.contains('inquiry-action-table') || table.getAttribute('data-virtual') === '1';
      var virtualState = { rowHeight: 44, topPad: null, bottomPad: null, raf: 0, enabled: false, heightLocked: false, _first: -1, _last: -1, _scroller: null, _viewH: 0, _scrollDelta: 0, _pageRows: null };

      var colFilters = {};

      // Ensure sticky classes on action header/cells if present
      if (table.tHead && table.tHead.rows[0]) {
        qa('th', table.tHead.rows[0]).forEach(function (th) {
          var txt = (th.textContent || '').trim().toLowerCase();
          if (th.classList.contains('col-action') || txt === 'action' || txt === 'actions') {
            th.classList.add('col-action');
          }
        });
      }

      // Per-column filters under headers (skip if filter-row or existing filters present)
      var hasFilterRow = table.tHead && qa('tr.filter-row', table.tHead).length > 0;
      var hasInlineFilters = table.tHead && table.tHead.querySelector('.enterprise-col-filter, .col-filter');

      if (!hasFilterRow && !hasInlineFilters && table.tHead && table.tHead.rows[0]) {
        qa('th', table.tHead.rows[0]).forEach(function (th, i) {
          if (th.classList.contains('col-action') || th.classList.contains('col-sticky')) return;
          if (th.querySelector('.enterprise-col-filter')) return;
          var label = (th.textContent || '').trim();
          if (!label) return;
          th.innerHTML = '';
          var wrap = document.createElement('div');
          wrap.className = 'enterprise-th-wrap';
          var lab = document.createElement('div');
          lab.className = 'enterprise-th-label';
          lab.textContent = label;
          var inp = document.createElement('input');
          inp.type = 'search';
          inp.className = 'form-control form-control-sm enterprise-col-filter';
          inp.placeholder = 'Filter…';
          inp.setAttribute('aria-label', 'Filter ' + label);
          inp.dataset.colIndex = String(i);
          var debounceTimer;
          inp.addEventListener('input', function () {
            var cidx = Number(inp.dataset.colIndex);
            colFilters[cidx] = (inp.value || '').toLowerCase().trim();
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(function () { page = 1; renderPage(); }, 160);
          });
          inp.addEventListener('click', function (e) { e.stopPropagation(); });
          wrap.appendChild(lab);
          wrap.appendChild(inp);
          th.appendChild(wrap);
        });
      }

      // Wire existing .col-filter / filter-row inputs
      if (hasFilterRow || hasInlineFilters) {
        qa('.col-filter, .enterprise-col-filter', table.tHead || table).forEach(function (inp) {
          var cidx = inp.dataset.col != null ? Number(inp.dataset.col) : Number(inp.dataset.colIndex);
          if (isNaN(cidx)) {
            var th = inp.closest('th');
            if (th && th.parentElement) {
              cidx = Array.prototype.indexOf.call(th.parentElement.children, th);
            }
          }
          if (isNaN(cidx)) return;
          inp.dataset.colIndex = String(cidx);
          var handler = function () {
            colFilters[cidx] = (inp.value || '').toLowerCase().trim();
            page = 1;
            renderPage();
          };
          inp.addEventListener('input', handler);
          inp.addEventListener('change', handler);
        });
      }

      // Default page size: larger for inquiry pipeline to maximize rows
      var defaultPageSize = (tableId === 'inquiryGrid') ? 100 : 40;
      var pageSize = defaultPageSize;
      var page = 1;
      var filterText = '';

      try {
        var saved = JSON.parse(localStorage.getItem(storageKey) || '{}');
        if (saved.pageSize) pageSize = Number(saved.pageSize) || defaultPageSize;
        if (Array.isArray(saved.hiddenColumns)) {
          saved.hiddenColumns.forEach(function (idx) {
            hideColumn(table, idx, true);
          });
        }
      } catch (_) { /* ignore */ }

      function saveLayout() {
        var hidden = [];
        if (table.tHead) {
          qa('th', table.tHead.rows[0] || table.tHead).forEach(function (th, i) {
            if (th.classList.contains('col-action') || th.classList.contains('col-sticky')) return;
            if (th.style.display === 'none') hidden.push(i);
          });
        }
        try {
          localStorage.setItem(storageKey, JSON.stringify({ pageSize: pageSize, hiddenColumns: hidden }));
        } catch (_) { /* ignore */ }
      }

      function hideColumn(tbl, index, hide) {
        qa('tr', tbl).forEach(function (tr) {
          var cell = tr.children[index];
          if (!cell) return;
          if (cell.classList.contains('col-action') || cell.classList.contains('col-sticky')) return;
          cell.style.display = hide ? 'none' : '';
        });
      }

      function visibleRows() {
        var qText = (filterText || '').toLowerCase().trim();
        return rows.filter(function (r) {
          if (qText && (r.textContent || '').toLowerCase().indexOf(qText) < 0) return false;
          for (var idx in colFilters) {
            if (!Object.prototype.hasOwnProperty.call(colFilters, idx)) continue;
            var fv = colFilters[idx];
            if (!fv) continue;
            var cell = r.children[Number(idx)];
            var cellText = cell ? (cell.textContent || '').toLowerCase() : '';
            if (cellText.indexOf(fv) < 0) return false;
          }
          return true;
        });
      }

      function buildPageNumbers(pages) {
        var container = q('[data-table-pages="' + tableId + '"]');
        if (!container) return;
        container.innerHTML = '';
        if (pages <= 1) return;

        function addBtn(label, targetPage, disabled, active) {
          var btn = document.createElement('button');
          btn.type = 'button';
          btn.className = 'btn btn-sm ' + (active ? 'btn-primary' : 'btn-outline-secondary');
          btn.textContent = label;
          btn.disabled = !!disabled;
          if (!disabled && !active) {
            btn.addEventListener('click', function () {
              page = targetPage;
              renderPage();
            });
          }
          container.appendChild(btn);
        }

        var windowSize = 5;
        var start = Math.max(1, page - Math.floor(windowSize / 2));
        var end = Math.min(pages, start + windowSize - 1);
        start = Math.max(1, end - windowSize + 1);

        if (start > 1) {
          addBtn('1', 1, false, page === 1);
          if (start > 2) {
            var ell = document.createElement('span');
            ell.className = 'small text-muted px-1';
            ell.textContent = '…';
            container.appendChild(ell);
          }
        }
        for (var p = start; p <= end; p++) {
          addBtn(String(p), p, false, p === page);
        }
        if (end < pages) {
          if (end < pages - 1) {
            var ell2 = document.createElement('span');
            ell2.className = 'small text-muted px-1';
            ell2.textContent = '…';
            container.appendChild(ell2);
          }
          addBtn(String(pages), pages, false, page === pages);
        }
      }

      function ensureVirtualPads() {
        if (!virtualState.topPad) {
          virtualState.topPad = document.createElement('tr');
          virtualState.topPad.className = 'virtual-pad virtual-pad-top';
          var tdT = document.createElement('td');
          tdT.colSpan = 99;
          tdT.style.cssText = 'padding:0;border:0;height:0;line-height:0;font-size:0;';
          virtualState.topPad.appendChild(tdT);
        }
        if (!virtualState.bottomPad) {
          virtualState.bottomPad = document.createElement('tr');
          virtualState.bottomPad.className = 'virtual-pad virtual-pad-bottom';
          var tdB = document.createElement('td');
          tdB.colSpan = 99;
          tdB.style.cssText = 'padding:0;border:0;height:0;line-height:0;font-size:0;';
          virtualState.bottomPad.appendChild(tdB);
        }
      }

      function measureRowHeight(sampleRow) {
        if (virtualState.heightLocked) return virtualState.rowHeight;
        if (!sampleRow) return virtualState.rowHeight || 44;
        // offsetHeight is cheaper than getBoundingClientRect for this case
        var h = sampleRow.offsetHeight;
        if (h && h > 20) {
          virtualState.rowHeight = h;
          virtualState.heightLocked = true; // lock after first good measure
        }
        return virtualState.rowHeight || 44;
      }

      function getScrollParent() {
        if (virtualState._scroller && virtualState._scroller.isConnected) {
          return virtualState._scroller;
        }
        virtualState._scroller = table.closest('.table-responsive, .enterprise-table-scroll, .inquiry-grid') || table.parentElement;
        return virtualState._scroller;
      }

      function enableVirtualViewport(scroller) {
        if (!scroller) return;
        if (scroller.getAttribute('data-virtual-scroll') === '1') return;
        scroller.setAttribute('data-virtual-scroll', '1');
        scroller.classList.add('virtual-scroll-port');
        scroller.style.maxHeight = scroller.style.maxHeight || 'min(70vh, calc(100vh - 260px))';
        scroller.style.overflowY = 'auto';
        // contain layout/paint for cheaper scroll
        scroller.style.contain = 'content';

        var lastScrollTop = 0;
        var stickyTimer = 0;
        scroller.addEventListener('scroll', function () {
          if (!virtualState.enabled) return;
          var st = scroller.scrollTop;
          // direction / speed hint for buffer
          virtualState._scrollDelta = Math.abs(st - lastScrollTop);
          lastScrollTop = st;
          if (virtualState.raf) return; // coalesce to 1 frame
          virtualState.raf = requestAnimationFrame(function () {
            virtualState.raf = 0;
            renderVirtualWindow(false);
            // Sticky only after scroll settles (expensive)
            clearTimeout(stickyTimer);
            stickyTimer = setTimeout(function () {
              if (virtualState.enabled) forceStickyActionReflow();
            }, 120);
          });
        }, { passive: true });
      }

      function renderVirtualWindow(force) {
        var vis = virtualState._pageRows || [];
        var total = vis.length;
        var scroller = getScrollParent();
        if (!scroller || total === 0) return;
        ensureVirtualPads();

        var rh = virtualState.rowHeight || 44;
        var scrollTop = scroller.scrollTop || 0;
        var viewH = virtualState._viewH || scroller.clientHeight || 400;
        // cache clientHeight; refresh occasionally
        if (force || !virtualState._viewH) virtualState._viewH = scroller.clientHeight || 400;
        viewH = virtualState._viewH;

        // Adaptive buffer: smaller while scrolling fast
        var fast = (virtualState._scrollDelta || 0) > 80;
        var buffer = fast ? 4 : 10;

        var first = Math.max(0, Math.floor(scrollTop / rh) - buffer);
        var last = Math.min(total, Math.ceil((scrollTop + viewH) / rh) + buffer);
        if (last <= first) last = Math.min(total, first + 15);

        // Skip DOM work if window unchanged
        if (!force && virtualState._first === first && virtualState._last === last) {
          return;
        }
        virtualState._first = first;
        virtualState._last = last;

        var topH = first * rh;
        var bottomH = Math.max(0, (total - last) * rh);

        // Single-pass DocumentFragment rebuild (only when window changed).
        // Skipping unchanged windows + deferred sticky is the main win;
        // fragment swap avoids layout thrash from many individual inserts.
        var frag = document.createDocumentFragment();
        virtualState.topPad.firstChild.style.height = topH + 'px';
        virtualState.bottomPad.firstChild.style.height = bottomH + 'px';
        frag.appendChild(virtualState.topPad);
        for (var j = first; j < last; j++) {
          vis[j].style.display = '';
          frag.appendChild(vis[j]);
        }
        frag.appendChild(virtualState.bottomPad);
        // replaceChildren is fastest when available
        if (typeof tbody.replaceChildren === 'function') {
          tbody.replaceChildren(frag);
        } else {
          while (tbody.firstChild) tbody.removeChild(tbody.firstChild);
          tbody.appendChild(frag);
        }

        if (!virtualState.heightLocked && first < total) {
          var prevRh = rh;
          measureRowHeight(vis[first]);
          if (virtualState.rowHeight && Math.abs(virtualState.rowHeight - prevRh) > 2) {
            virtualState._first = -1;
            renderVirtualWindow(true);
          }
        }
      }

      function renderPage() {
        var vis = visibleRows();
        var total = vis.length;
        var pages = Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
        if (page > pages) page = pages;
        if (page < 1) page = 1;
        var start = (page - 1) * pageSize;
        var end = Math.min(start + pageSize, total);
        var pageRows = vis.slice(start, end);

        virtualState.enabled = useVirtual && pageRows.length > 40;
        virtualState._pageRows = pageRows;
        virtualState._first = -1;
        virtualState._last = -1;
        virtualState._viewH = 0;
        // keep height lock across pages if already measured
        // virtualState.heightLocked stays

        var scroller = getScrollParent();
        if (virtualState.enabled) {
          enableVirtualViewport(scroller);
          while (tbody.firstChild) tbody.removeChild(tbody.firstChild);
          if (pageRows.length) {
            pageRows[0].style.display = '';
            tbody.appendChild(pageRows[0]);
            measureRowHeight(pageRows[0]);
            tbody.removeChild(pageRows[0]);
          }
          if (scroller) scroller.scrollTop = 0;
          renderVirtualWindow(true);
          // one sticky pass after initial mount
          requestAnimationFrame(function () { forceStickyActionReflow(); });
        } else {
          rows.forEach(function (r) {
            if (!r.parentNode) tbody.appendChild(r);
            r.style.display = 'none';
          });
          qa('tr.virtual-pad', tbody).forEach(function (p) { p.remove(); });
          pageRows.forEach(function (r) { r.style.display = ''; });
          forceStickyActionReflow();
        }

        var info = q('[data-table-info="' + tableId + '"]');
        if (info) {
          info.textContent = total === 0
            ? 'No records'
            : ('Showing ' + (total ? start + 1 : 0) + '–' + Math.min(end, total) + ' of ' + total
               + (virtualState.enabled ? ' · virtual scroll' : ''));
        }
        var pageLabel = q('[data-table-page="' + tableId + '"]');
        if (pageLabel) pageLabel.textContent = page + ' / ' + pages;

        buildPageNumbers(pages);
      }

      // Wire toolbar controls if present
      var sizeSel = q('[data-table-pagesize="' + tableId + '"]');
      if (sizeSel) {
        sizeSel.value = String(pageSize);
        sizeSel.addEventListener('change', function () {
          pageSize = Number(sizeSel.value) || defaultPageSize;
          page = 1;
          saveLayout();
          renderPage();
        });
      }
      var search = q('[data-table-search="' + tableId + '"]');
      if (search) {
        var debounce;
        search.addEventListener('input', function () {
          clearTimeout(debounce);
          debounce = setTimeout(function () {
            filterText = search.value || '';
            page = 1;
            renderPage();
          }, 180);
        });
      }
      var prev = q('[data-table-prev="' + tableId + '"]');
      var next = q('[data-table-next="' + tableId + '"]');
      if (prev) prev.addEventListener('click', function () { if (page > 1) { page--; renderPage(); } });
      if (next) next.addEventListener('click', function () {
        var pages = Math.max(1, Math.ceil(visibleRows().length / pageSize));
        if (page < pages) { page++; renderPage(); }
      });

      var resetBtn = q('[data-table-reset="' + tableId + '"]');
      if (resetBtn) {
        resetBtn.addEventListener('click', function () {
          try { localStorage.removeItem(storageKey); } catch (_) { /* ignore */ }
          qa('tr', table).forEach(function (tr) {
            qa('th,td', tr).forEach(function (cell) {
              if (!cell.classList.contains('col-action')) cell.style.display = '';
            });
          });
          pageSize = defaultPageSize;
          page = 1;
          filterText = '';
          colFilters = {};
          qa('.enterprise-col-filter, .col-filter', table).forEach(function (inp) { inp.value = ''; });
          if (sizeSel) sizeSel.value = String(defaultPageSize);
          if (search) search.value = '';
          renderPage();
        });
      }

      // Column chooser
      var chooser = q('[data-table-chooser="' + tableId + '"]');
      if (chooser && table.tHead && !chooser.querySelector('.dropdown-menu')) {
        var menu = document.createElement('div');
        menu.className = 'dropdown-menu dropdown-menu-end p-2';
        menu.style.minWidth = '220px';
        menu.addEventListener('click', function (e) { e.stopPropagation(); });
        qa('th', table.tHead.rows[0] || table.tHead).forEach(function (th, i) {
          if (th.classList.contains('col-action') || th.classList.contains('col-sticky')) return;
          var labelEl = th.querySelector('.enterprise-th-label');
          var label = labelEl ? labelEl.textContent.trim() : (th.textContent || ('Column ' + (i + 1))).trim();
          if (!label) return;
          var item = document.createElement('label');
          item.className = 'dropdown-item d-flex align-items-center gap-2 mb-0';
          item.style.cursor = 'pointer';
          var cb = document.createElement('input');
          cb.type = 'checkbox';
          cb.className = 'form-check-input m-0';
          cb.checked = th.style.display !== 'none';
          cb.addEventListener('change', function () {
            hideColumn(table, i, !cb.checked);
            saveLayout();
          });
          item.appendChild(cb);
          var span = document.createElement('span');
          span.textContent = label;
          item.appendChild(span);
          menu.appendChild(item);
        });
        chooser.appendChild(menu);
      }

      // Ensure page-number container exists next to pagination controls
      var pageLabelEl = q('[data-table-page="' + tableId + '"]');
      if (pageLabelEl && !q('[data-table-pages="' + tableId + '"]')) {
        var pagesWrap = document.createElement('div');
        pagesWrap.className = 'd-flex align-items-center gap-1 flex-wrap enterprise-page-numbers';
        pagesWrap.setAttribute('data-table-pages', tableId);
        if (pageLabelEl.parentNode) {
          pageLabelEl.parentNode.insertBefore(pagesWrap, pageLabelEl.nextSibling);
        }
      }

      renderPage();
      document.dispatchEvent(new CustomEvent('pnx-table-ready', { detail: { tableId: tableId } }));
    });
  }

  /* ---------- Web Push subscription ---------- */
  function urlBase64ToUint8Array(base64String) {
    var padding = '='.repeat((4 - (base64String.length % 4)) % 4);
    var base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    var raw = atob(base64);
    var output = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) output[i] = raw.charCodeAt(i);
    return output;
  }

  function initWebPushSubscribe() {
    if (!document.body.classList.contains('crm-app-body')) return;
    if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) return;
    // Prefer unified helper from crm-ui.js when present
    if (window.ProfitNxDesktopNotify && typeof window.ProfitNxDesktopNotify.subscribePush === 'function') {
      setTimeout(function () { window.ProfitNxDesktopNotify.subscribePush(); }, 1200);
      setTimeout(function () {
        if (!window.__pnxPushSubscribed) window.ProfitNxDesktopNotify.subscribePush();
      }, 6000);
      document.addEventListener('click', function once() {
        if (window.ProfitNxDesktopNotify) window.ProfitNxDesktopNotify.subscribePush();
        document.removeEventListener('click', once);
      }, { once: true });
      return;
    }

    var subscribing = false;
    async function subscribe() {
      // Avoid overlapping attempts (click handler + timers can all fire close together).
      if (subscribing) return;
      subscribing = true;
      try {
        var keyRes = await fetch('/Push/PublicKey', {
          credentials: 'same-origin',
          headers: { 'X-Requested-With': 'XMLHttpRequest' }
        });
        if (!keyRes.ok) { console.warn('[ProfitNx push] PublicKey request failed', keyRes.status); return; }
        var keyData = await keyRes.json();
        if (!keyData.configured || !keyData.publicKey) { console.warn('[ProfitNx push] Web Push not configured on server'); return; }

        var reg = await navigator.serviceWorker.ready;
        var existing = await reg.pushManager.getSubscription();
        if (!existing) {
          if (Notification.permission === 'denied') { console.warn('[ProfitNx push] Notification permission denied'); return; }
          if (Notification.permission === 'default') {
            var perm = await Notification.requestPermission();
            if (perm !== 'granted') { console.warn('[ProfitNx push] Permission not granted:', perm); return; }
          }
          existing = await reg.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: urlBase64ToUint8Array(keyData.publicKey)
          });
        }

        var json = existing.toJSON();
        var subRes = await fetch('/Push/Subscribe', {
          method: 'POST',
          credentials: 'same-origin',
          headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
          body: JSON.stringify({
            endpoint: json.endpoint,
            keys: {
              p256dh: json.keys && json.keys.p256dh,
              auth: json.keys && json.keys.auth
            }
          })
        });
        if (!subRes.ok) console.warn('[ProfitNx push] /Push/Subscribe failed', subRes.status);
        else window.__pnxPushSubscribed = true;
      } catch (e) {
        // Push is optional for in-app usage (in-page desktop notifications still work
        // while the tab is open/minimized) - but log so it's diagnosable in devtools.
        console.warn('[ProfitNx push] subscribe attempt failed', e);
      } finally {
        subscribing = false;
      }
    }

    document.addEventListener('click', function once() {
      document.removeEventListener('click', once);
      setTimeout(subscribe, 400);
    }, { once: true });
    document.addEventListener('touchstart', function once() {
      document.removeEventListener('touchstart', once);
      setTimeout(subscribe, 400);
    }, { once: true, passive: true });
    // Try soon after load (covers users who don't interact right away), and
    // keep retrying periodically in case the first attempt happened before
    // permission was granted, or a transient network error hit /Push/Subscribe.
    setTimeout(subscribe, 2000);
    setInterval(function () {
      if (!window.__pnxPushSubscribed) subscribe();
    }, 30 * 60 * 1000);
  }

  /* ---------- Inquiry pipeline: maximize visible rows ---------- */
  function initInquiryPipelineMaximize() {
    var wrap = q('.inquiry-grid.table-responsive, .table-responsive.inquiry-grid');
    if (!wrap) return;
    function applyHeight() {
      // Leave room for header, toolbar, cards, pagination (~240–300px depending on viewport).
      // Reduced from 280/320 now that the toolbar no longer wraps onto two
      // lines (search box + page-size selector fixed to sit on one row),
      // which frees up a row's worth of height for the table itself.
      var reserved = window.innerWidth < 992 ? 300 : 240;
      var h = Math.max(360, window.innerHeight - reserved);
      wrap.style.maxHeight = h + 'px';
      wrap.style.overflowY = 'auto';
    }
    applyHeight();
    window.addEventListener('resize', function () {
      clearTimeout(window.__pnxInqH);
      window.__pnxInqH = setTimeout(applyHeight, 100);
    });
  }

  function boot() {
    initSidebarCollapse();
    enhanceBackgroundNotifications();
    initEnterpriseTables();
    initWebPushSubscribe();
    initInquiryPipelineMaximize();
    setTimeout(forceStickyActionReflow, 50);
    setTimeout(forceStickyActionReflow, 300);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }

  window.addEventListener('resize', function () {
    clearTimeout(window.__pnxStickyT);
    window.__pnxStickyT = setTimeout(forceStickyActionReflow, 80);
  });
  document.addEventListener('pnx-theme-changed', forceStickyActionReflow);
  document.addEventListener('pnx-table-ready', forceStickyActionReflow);

  // Extra safety net for the frozen Action column: re-apply the lock on
  // every horizontal scroll tick (rAF-throttled) and whenever the table
  // body's rows change (pagination / filtering that doesn't fire
  // pnx-table-ready), so the freeze can never drift out of sync.
  (function watchStickyActionColumn() {
    var wrap = q('.inquiry-grid.table-responsive, .table-responsive.inquiry-grid');
    if (!wrap) return;
    var ticking = false;
    wrap.addEventListener('scroll', function () {
      if (ticking) return;
      ticking = true;
      window.requestAnimationFrame(function () {
        forceStickyActionReflow();
        ticking = false;
      });
    }, { passive: true });

    var table = q('table.inquiry-action-table', wrap);
    var tbody = table && table.tBodies[0];
    if (tbody && window.MutationObserver) {
      var mo = new MutationObserver(function () {
        clearTimeout(window.__pnxStickyMoT);
        window.__pnxStickyMoT = setTimeout(forceStickyActionReflow, 30);
      });
      mo.observe(tbody, { childList: true });
    }
  })();
})();


/* ===== Nuclear Action-column freeze (ancestors + reflow) ===== */
(function () {
  function unlockAncestors(el) {
    var node = el;
    var guard = 0;
    while (node && node !== document.documentElement && guard++ < 40) {
      if (node.nodeType === 1) {
        try {
          var cs = window.getComputedStyle(node);
          var ox = cs.overflowX, oy = cs.overflowY, ov = cs.overflow;
          if (ox !== 'visible' || oy !== 'visible' || (ov && ov !== 'visible')) {
            // Never kill overflow on the intended horizontal scrollport
            if (!node.classList.contains('inquiry-grid') &&
                !node.classList.contains('table-responsive') &&
                !node.classList.contains('enterprise-table-scroll')) {
              node.style.setProperty('overflow', 'visible', 'important');
              node.style.setProperty('overflow-x', 'visible', 'important');
              node.style.setProperty('overflow-y', 'visible', 'important');
            }
          }
          if (cs.transform && cs.transform !== 'none') {
            node.style.setProperty('transform', 'none', 'important');
          }
          if (cs.filter && cs.filter !== 'none') {
            node.style.setProperty('filter', 'none', 'important');
          }
        } catch (e) {}
      }
      node = node.parentElement;
    }
  }

  function freezeActionColumns() {
    document.querySelectorAll('.table-responsive.inquiry-grid, .inquiry-grid.table-responsive, .enterprise-table-scroll').forEach(function (scroller) {
      unlockAncestors(scroller.parentElement || scroller);
      scroller.style.setProperty('overflow-x', 'auto', 'important');
      scroller.style.setProperty('overflow-y', 'visible', 'important');
      scroller.style.setProperty('position', 'relative', 'important');
      scroller.setAttribute('data-sticky-ready', '1');

      var table = scroller.querySelector('table.inquiry-action-table');
      if (!table) return;
      table.style.setProperty('border-collapse', 'separate', 'important');
      table.style.setProperty('border-spacing', '0', 'important');

      var cells = table.querySelectorAll('th.col-action, td.col-action, td.inquiry-action-td');
      if (!cells.length) {
        cells = table.querySelectorAll('thead th:last-child, tbody td:last-child');
      }
      cells.forEach(function (cell) {
        cell.style.setProperty('position', 'sticky', 'important');
        cell.style.setProperty('right', '0px', 'important');
        cell.style.setProperty('left', 'auto', 'important');
        cell.style.setProperty('z-index', '60', 'important');
        // force opaque bg from computed theme if empty
        var bg = window.getComputedStyle(cell).backgroundColor;
        if (!bg || bg === 'rgba(0, 0, 0, 0)' || bg === 'transparent') {
          cell.style.setProperty('background-color', '#ffffff', 'important');
        }
      });
    });
  }

  function bootSticky() {
    freezeActionColumns();
    setTimeout(freezeActionColumns, 100);
    setTimeout(freezeActionColumns, 400);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', bootSticky);
  } else {
    bootSticky();
  }
  window.addEventListener('resize', function () {
    clearTimeout(window.__pnxFreezeT);
    window.__pnxFreezeT = setTimeout(freezeActionColumns, 80);
  });
  document.addEventListener('pnx-theme-changed', freezeActionColumns);
  document.addEventListener('pnx-table-ready', freezeActionColumns);
})();
