# Changes 2026-09-10 – Update Note clear + single Save

## Point 1 – Update Note clears after Save
- After successful Save on **Update Note** modal:
  - Textarea is cleared
  - Visible "Update note" field is cleared
  - Next open starts empty (ready for a new note)
  - Hidden form field still keeps the last saved note so **Save Status** validation still passes

## Point 2 – Single Save (no multiple clicks)
- Root cause: status forms live inside modals rendered **after** the old JS ran, so submit handlers never attached.
- Fixed with **event delegation** on `document` for `.quick-status-form` submit/change.
- Save Status button disables immediately on valid submit (`Saving…`) to prevent double post.
- Update Note Save ignores clicks while already saving (`disabled` guard).
- On status modal open: form re-bound, fields synced, Save button re-enabled if needed.

## Space / more rows
- Tighter row padding, smaller badges/icons, slightly denser Action column so more rows fit on screen.

## Files
- `Views/Inquiry/Index.cshtml`
- `wwwroot/css/action-column-freeze-fix-2026-09-10.css`
