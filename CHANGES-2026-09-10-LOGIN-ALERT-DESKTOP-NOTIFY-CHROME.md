# Changes – 2026-09-10: Login Alert desktop notifications + Chrome support

## 1. Login Alerts → OS desktop notification (same wording as in-app card)

When pending items are only **User Login / User Logout** alerts, the desktop
notification now matches the in-app **Login Alerts** card:

- **Title:** `User Login Alert` / `User Logout Alert` (single) or `Login Alerts (N)` (batch)
- **Body:** message + sender + time (or first few messages joined for batch)
- **Click URL:** Login History Report (`actionUrl`), not generic Notifications

Previously every pending item used the generic title `ProfitNx CRM - Task Assigned`.

**File:** `Views/Shared/_Layout.cshtml` → `notifyIfMinimized()`

## 2. Desktop notifications work in Google Chrome (as well as Edge)

Chrome was missing notifications while Edge worked, mainly due to:

1. **Background detection** – Chrome on Windows can keep `document.hasFocus()` true
   while the window is minimized. Detection now prioritizes `document.hidden` /
   `visibilityState` and also treats discarded tabs as background.
2. **Service Worker path** – Prefer `registration.showNotification()` (works without
   a claimed controller). Still postMessage to the active SW as a second path.
3. **SW register** – Explicit `scope: '/'` + SKIP_WAITING so Chrome attaches the
   worker without requiring a full page reload.
4. **Permission gesture** – `touchstart` added alongside click/keydown so the
   notification permission prompt can appear on all input types (Chrome requires
   a user gesture).

**Files:**
- `wwwroot/js/crm-ui.js` → `ProfitNxDesktopNotify` (isBackground, ensureSw, show, boot)
- `wwwroot/service-worker.js` → cache version `push-v9` (forces SW update)

## How to verify

1. Chrome + Edge: login, click **Enable** on the notifications bar (or Allow when prompted).
2. Console: `[ProfitNx push] Subscribed OK` (optional Web Push; in-page desktop notify works without it).
3. Have another user login/logout (or trigger a Login Alert). Minimize the CRM tab/window.
4. OS notification should appear with **Login Alert(s)** title and the same text as the purple in-app card.
5. Click the OS notification → opens Login History Report.

If Chrome still blocks: Site settings → Notifications → Allow for your CRM origin (must be HTTPS or localhost).
