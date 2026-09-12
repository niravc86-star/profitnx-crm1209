# Changes – 2026-09-10 (v2): Desktop notifications for any type + true Action column freeze

## Point 1 — Desktop notification when browser is minimized (any notification type)

**What was missing:** the general "pending task" desktop-notification flow
(covers every notification type — inquiry, implementation, login alert, low
stock, etc., same source as the notification bell) only fired an OS
notification **once**, the first time it saw a brand-new item. If the CRM
window was already minimized before that poll ran, or the popup was missed,
no further desktop alert ever appeared for it — unlike the "Pending Inquiry"
attention popup, which already re-notified every 90 seconds.

**Fix (`Views/Shared/_Layout.cshtml`):**
- The task-flash poll now re-sends a desktop notification every ~90s while
  the window stays minimized/unfocused **and there is still at least one
  unread notification of any type**, matching the attention flow.
- Notification-permission is now requested on `click`, `keydown`, **and
  `touchstart`** (was missing touch, so it could stay stuck on "default"
  forever on a touch device) — same fix applied to the chat and attention
  notifiers in `crm-ui.js`.

**Hardened the real Web Push path too (`wwwroot/js/enterprise-ui-2026-09-08.js`):**
Web Push (service-worker `push` event, `docs/WEB-PUSH.md`) is what keeps
working even when the browser fully throttles/suspends a minimized tab or
the tab itself is closed — the in-page timers above can only help while the
tab is still alive and not too heavily throttled. `App_Data/push-subscriptions.json`
was empty, meaning no device had ever completed a subscription, so:
- `subscribe()` now runs ~2s after load (was 8s) and also on `touchstart`.
- It **retries every 30 minutes** if it hasn't successfully subscribed yet,
  instead of only trying once per page load.
- Failures are now logged to the console (`[ProfitNx push] ...`) instead of
  being silently swallowed, so a real failure (denied permission, server not
  configured, network error) is visible in DevTools instead of looking like
  "nothing happened."

## Point 2 — Action column: hard freeze, no scroll, no bleed-through

**Root cause found:** the Inquiry Pipeline table renders the Action cell
with **two different markup branches** depending on role:
- Support / Support Head rows: `<td class="col-action"><div class="qs-action-cell">…</div></td>`
- Everyone else: `<td class="inquiry-action-td col-action"><div class="qs-action-cell qs-action-compact">…</div></td>`

Only the second branch had the `.qs-action-compact` class that centers and
wraps the icon row. The first branch's plain `.qs-action-cell` used
`justify-content: flex-end` with no wrap, so its 3 icon buttons could sit
just wide enough to visually spill past the frozen cell's right edge —
looking exactly like "the freeze is filled over / not really frozen" during
horizontal scroll, even though `position: sticky` itself was working.

**Fix:**
- `wwwroot/css/action-column-freeze-fix-2026-09-10.css` — added `overflow:
  hidden` + `box-sizing: border-box` to the frozen header/body cells, and
  forced **every** action-row wrapper (`.qs-action-cell` and
  `.qs-action-compact`, in both markup branches) to `flex-wrap: wrap` and
  `max-width: 100%` so content can never escape the frozen box.
- `wwwroot/js/enterprise-ui-2026-09-08.js` — `forceStickyActionReflow()` now
  hard-locks the Action column to one exact pixel width
  (`width`/`min-width`/`max-width` all `128px`) on every header **and**
  body cell, plus `overflow: hidden`, so the column can never differ in
  width row-to-row regardless of which markup branch rendered it. It now
  also re-applies on every horizontal scroll tick (rAF-throttled) and via a
  `MutationObserver` on the table body, so pagination/filtering re-renders
  can't drift out of sync even if they don't dispatch `pnx-table-ready`.

## How to verify

**Point 1:** Log in as a user, minimize/switch away from the browser, and
have another user assign/forward something to you. A desktop notification
should appear promptly; if nothing new happens it should repeat every ~90s
while still unread and the window stays backgrounded. Check the browser
console for `[ProfitNx push]` lines if Web Push itself needs a look.

**Point 2:** Open the Inquiry Pipeline as a Support/SupportHead user (the
previously-broken branch) and as an Admin/User. Scroll the grid horizontally
— the Action column (icon buttons) must stay perfectly fixed at the right
edge with no visible drift, gap, or other columns' text showing through it,
for every row.
