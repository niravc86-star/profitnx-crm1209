# Changes – 2026-09-10 Action Column Compact + True Sticky

## What changed

### 1. Action column – compact (icon buttons only)
- Inquiry Pipeline Action cell now shows **only icon buttons**:
  - View / Timeline (eye)
  - Edit (pencil)
  - Delete (trash)
  - Update Status (purple gradient arrow) – opens modal
- Full status form (Select Status, Genuine, Forward, Sold fields, Note, Action date, Save Status) moved into a **Bootstrap modal** per row.
- Row height is much smaller → **more rows visible** on screen.

### 2. True freeze (Excel-style)
- Hard CSS fix so Action column stays fixed on the **right** while other columns scroll horizontally.
- Parents forced to `overflow: visible`.
- Only `.table-responsive.inquiry-grid` scrolls horizontally.
- `border-collapse: separate`, `position: sticky; right: 0; z-index: 50`.
- Opaque theme backgrounds so content never shows through the frozen column.
- JS reflow uses `setProperty(..., 'important')`.

### 3. Files touched
- `Views/Inquiry/Index.cshtml` – Action cell + status modal
- `wwwroot/css/enterprise-ui-2026-09-08.css` – sticky + compact icon styles
- `wwwroot/js/enterprise-ui-2026-09-08.js` – sticky reflow reinforcement

## How to verify
1. Open Inquiry Pipeline.
2. Horizontally scroll the table – Action column must stay fixed on the right.
3. Action column should show only small icon buttons.
4. Click the purple status icon → modal opens with full status form; Save Status still works.
5. More rows should be visible without changing page size.
