# Changes 2026-09-10 — Reports pagination/filters + Sticky Action + Inquiry max rows

## Point 1 — Reports: Pagination + Column-wise Filters
- Enhanced `wwwroot/js/enterprise-ui-2026-09-08.js`:
  - Page size selector, Previous/Next, **page number buttons**
  - Per-column filters under every header (text search; respects existing `.filter-row` / `.col-filter`)
  - Independent column filters + global quick search; real-time (debounced)
  - Works on `enterprise-data-table`, `pg-table[data-table-id]`, `report-table[data-table-id]`
- Applied enterprise toolbar + table + pagination to:
  - Inquiry Pipeline (already present; improved page sizes + page numbers)
  - Reports: support status grid, detailed pipeline grid, source report, training report
  - Admin Users, Product Master, Partner Master, Scheme Master
  - Implementation list, Commission report, Stock list, Stock Amount Report
- CSS: filter inputs under headers, page-number button styles, pagination bar polish

## Point 2 — Sticky Action + Inquiry Pipeline density
- Sticky Action column (right) hardened for:
  - `inquiry-action-table`, `enterprise-data-table`, `crm-data-table`, `pg-table`
  - Opaque theme backgrounds so content never shows through while scrolling
  - JS reflow on load / resize / theme change / table render (scope bug fixed)
- Inquiry Pipeline maximize rows:
  - Viewport-based `max-height` on `.inquiry-grid` (`calc(100vh - 280px)`) with vertical scroll
  - Compact row padding, tighter action panel controls
  - Default page size 100; options up to 1000 records
  - Sticky Action remains correct with many visible rows

## Notes
- Client-side pagination filters the already-rendered row set (Sheets-backed architecture).
- Existing server-side filter forms are unchanged.
- Design system, colors, and component styles preserved.
