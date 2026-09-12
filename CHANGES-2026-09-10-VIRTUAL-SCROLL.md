# Virtual scrolling – Inquiry Pipeline

## What
- Client-side **virtual scrolling** for `inquiry-action-table` when the current page has **more than 40 rows**.
- Only rows near the viewport are in the DOM; top/bottom spacer rows preserve scroll height.
- Toolbar option **All (virtual scroll)** (5000) loads the full filtered set into one page and scrolls virtually.
- Vertical viewport: `max-height: min(70vh, calc(100vh - 260px))` on the table scroll container.
- Horizontal scroll + sticky Action column still applied after each virtual window paint.

## Files
- `wwwroot/js/enterprise-ui-2026-09-08.js` – virtual window renderer
- `wwwroot/css/action-column-freeze-fix-2026-09-10.css` – `.virtual-scroll-port`
- `Views/Inquiry/Index.cshtml` – page-size option

## How to use
1. Open Inquiry Pipeline.
2. Set page size to 100+ or **All (virtual scroll)**.
3. Scroll vertically inside the table area — only visible rows stay mounted.
