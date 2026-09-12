# ProfitNx CRM — Enterprise UI Changes (2026-09-08)

## Architecture (unchanged)
- ASP.NET Core MVC + Razor views
- Google Sheets data store
- Bootstrap 5 + multi-theme CSS (classic / premium / midnight / saas / dompet / emerald / rosegold)
- PWA service worker for offline shell + desktop notifications
- Cookie auth + permission service

## Implemented in this pass

### 1. Sticky Action column
- Hardened CSS in `wwwroot/css/enterprise-ui-2026-09-08.css`
- Opaque sticky backgrounds for Light, Midnight, and Premium so scrolled cells never bleed through
- Higher z-index on header vs body cells
- Works with existing `inquiry-action-panel-2026-09-01.css`

### 2. User identity / Logout
- Logout is **icon-only** (`bi-box-arrow-right`)
- Tooltip + `aria-label="Logout"`
- Profile name remains **once** in the top-right profile summary (role under name)
- Lock overlay still shows name (intentional)

### 3. Notification phone number + background delivery
- **Midnight** and **Premium** theme overrides for `.attention-call` and attention cards (high-contrast cyan / gold pills)
- Light themes unchanged
- Attention / task-flash / chat pollers listen for `pnx-force-poll` and run on `visibilitychange` (including when tab is hidden)
- Service worker still handles `SHOW_NOTIFICATION` + click → focus/navigate
- **Limitation:** true delivery when the browser process is fully closed requires Web Push + VAPID keys + backend push subscription storage. Sheets-backed app does not have that path yet; in-app + desktop notifications while the tab exists (even minimized/background) are restored/strengthened.

### 4. Collapsible sidebar (Gmail-style)
- Desktop toggle button under brand logo
- Collapsed = icons only; expanded = icons + labels
- State persisted in `localStorage` key `profitnx.sidebar.collapsed`
- Mobile drawer behavior unchanged (existing `data-sidebar-toggle` + backdrop)
- Alt+[ keyboard shortcut on desktop

### 5. Dashboard consolidation
- **One** main menu entry: **Dashboard**
- If user has `dashboard.view` → Index
- Else if only `dashboard.live` → Live
- Separate “Live Dashboard” menu entry removed
- Live remains at `/Dashboard/Live` (bookmarks/permissions intact)
- Live tile added on main Dashboard Index when `dashboard.live` is allowed

### 6. Enterprise data table (Inquiry grid first)
- Shared helpers: `enterprise-ui-2026-09-08.js` + CSS
- Inquiry Index: toolbar (search, page size 10/25/50/100), column chooser, reset layout, pagination footer
- Layout persisted per **user + table id** in localStorage
- Action column cannot be hidden
- Client-side pagination/filter on already server-filtered rows (Google Sheets loads full filtered set; this avoids shipping a heavy grid library)

## New files
- `wwwroot/css/enterprise-ui-2026-09-08.css`
- `wwwroot/js/enterprise-ui-2026-09-08.js`

## Modified files
- `Views/Shared/_Layout.cshtml` — CSS/JS includes, logout, sidebar toggle, single Dashboard menu, notification force-poll
- `Views/Dashboard/Index.cshtml` — Live tile when permitted
- `Views/Inquiry/Index.cshtml` — enterprise toolbar + table classes + pagination
- `wwwroot/js/crm-ui.js` — `pnx-force-poll` for attention
- `wwwroot/service-worker.js` — cache version bump + new assets

## Recommended next steps (not fully done this pass)
1. Apply `enterprise-data-table` + toolbar pattern to other report tables (Reports, Admin Users, Stock, etc.).
2. Column-wise filter popovers per data type (text/date/select) on top of existing server filter forms.
3. Optional: user preference API on Sheets for layout persistence across devices.
4. Optional: Web Push (VAPID) if closed-browser notifications become a hard requirement.
5. Merge Live widgets into a single role-aware Dashboard shell if product wants zero separate Live route long-term.

## Acceptance (this pass)
- [x] Sticky Action hardened (themes)
- [x] Logout icon-only + tooltip
- [x] Profile name once in header
- [x] Phone number readable in Midnight + Premium
- [x] In-app attention modal preserved
- [x] Desktop notifications when tab minimized/background (browser-throttling aware)
- [x] Sidebar collapse + persist
- [x] Single Dashboard menu
- [x] Inquiry table: page size, search, column chooser, reset, sticky Action
- [ ] Full enterprise table on every report page
- [ ] Server-side pagination (Sheets architecture limits; client page of filtered set is used)
- [ ] Full column-wise filter popups on every column

## Follow-up pass (same day) — user-reported remaining issues

### Action column freeze (highest priority)
- Strengthened sticky rules with `will-change`, `translateZ(0)`, higher z-index
- Forced opaque backgrounds per theme so scrolled content never shows through
- JS reflow helper after load / resize / theme change so sticky paints correctly
- Isolated scroll containers; removed transform/filter on ancestors that break sticky

### Notifications (Midnight + Premium Elite)
- Expanded theme rules for notification tree list, date groups, tree items, update rows, badges, latest-update blocks
- Attention / pending cards fully readable (title, meta, phone pill already themed)

### Header / Logout after username removal
- Profile name fully hidden (only avatar initial remains)
- Spacing tightened in `.profile-area` / `.top-panel`
- Logout already theme-aware (light / midnight / premium / saas / etc.)

### Column filters
- Confirmed filters are injected *inside* each `<th>` under the header label
- CSS ensures full width + spacing under header text; Action column excluded

### Pagination
- Existing enterprise client-side pagination + page-size controls remain on Inquiry and any `enterprise-data-table`

### Verification checklist performed in this pass
- Sticky Action CSS specificity raised
- Notification tree + attention cards covered for both dark themes
- Header no longer leaves empty space from removed username
- No new duplicate components introduced

## Follow-up pass 2 (user feedback)

### 1. All Inquiry Pipeline — more rows by default
- Default page size raised from 20 → **100 records**
- Options now: 20 / 40 / 100 / 200 / 500
- JS default + Reset also use 100

### 2. Notification popup customer details (Midnight + Premium Elite)
- Full styling for `.attention-title`, `.attention-meta`, `.attention-status`, `.attention-pending`, `.attention-last`, icon, arrow, phone pill
- High-contrast colors so company name, person, city, phone, status, pending duration are all clearly readable

### 3. Logout button premium redesign
- Clear gradient + border so it is never blank on white/light themes
- Distinct hover (red tint) and solid presence on Midnight / Premium Elite
- Icon-only, consistent size with other top actions

### 4. Pending notification when browser minimized
- Stronger desktop notification path (SW controller → SW ready → page Notification)
- Permission request on first click/keydown
- Background poll every 15s while hidden; re-notify every 90s while still pending
- visibilitychange / blur / focus / pagehide / pnx-force-poll all trigger checks
- SW cache bumped to v4 so new JS loads
