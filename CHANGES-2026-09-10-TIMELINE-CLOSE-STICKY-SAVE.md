# Fixes 2026-09-10

## 1. Full Status Timeline scroll
- Edit page + pipeline history modal: `.timeline-list-scroll` with max-height ~60vh and overflow-y auto so 7+ updates all reachable via scroll.

## 2. Close – no separate Close reason textbox
- Removed visible Close reason field from Update Status modal.
- Note field is enough; on Close, note is copied to closeReason.
- Controller accepts note as closeReason when closeReason empty (defaults to "Closed").

## 3. Action column true freeze
- Nuclear JS: walks ancestors and forces overflow visible (except the table scrollport), then reapplies position:sticky; right:0 on every Action cell after load / resize / theme / table-ready.
- CSS scrollport + sticky rules retained.

## 4. Update Status – single Save
- If Update note is empty, auto-fills short note from selected status (e.g. "Status: Follow Up", "Closed", "Sold") so one Save Status click is enough — no forced second pass through Update Note modal.
