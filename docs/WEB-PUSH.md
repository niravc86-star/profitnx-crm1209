# Web Push (desktop notifications when tab is closed)

## Flow
1. Browser grants **Notification** permission.
2. Client calls `ProfitNxDesktopNotify.subscribePush()`:
   - `GET /Push/PublicKey` → VAPID public key
   - `pushManager.subscribe({ applicationServerKey })`
   - `POST /Push/Subscribe` with `{ endpoint, keys: { p256dh, auth } }`
3. Server stores subscription in `App_Data/push-subscriptions.json` (per user).
4. Server sends push via `IWebPushService.SendToUserAsync`:
   - CRM notification channel
   - `PendingAttentionPushWorker` (every ~2 minutes when pending set changes)
5. Service worker `push` event → `showNotification` (works with tab closed).

## VAPID keys
Configured in `appsettings.json` → `WebPush:PublicKey` / `PrivateKey`  
Fallback file: `App_Data/webpush-vapid.json`

## User steps
1. Login to CRM (HTTPS or localhost).
2. Click **Enable** on the notifications bar (or Allow in browser prompt).
3. Console should log: `[ProfitNx push] Subscribed OK`
4. Minimize or close the tab — pending inquiry / CRM alerts still arrive as OS notifications.

## API
| Endpoint | Method | Auth |
|----------|--------|------|
| `/Push/PublicKey` | GET | Authorize |
| `/Push/Subscribe` | POST JSON | Authorize |
| `/Push/Unsubscribe` | POST JSON | Authorize |
