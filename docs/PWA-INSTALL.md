# ProfitNx CRM — PWA Installation Setup

## What is included
- `wwwroot/manifest.webmanifest` — app name, icons, shortcuts, standalone display
- `wwwroot/service-worker.js` — offline shell cache + network fallback
- `wwwroot/offline.html` — offline screen
- Install button in top bar (phone icon) + install guidance modal
- iOS / Android meta tags for Add to Home Screen

## Server requirements
1. Serve over **HTTPS** (required on phones; `localhost` OK for testing).
2. Do not block `/manifest.webmanifest` or `/service-worker.js`.
3. After deploy, hard-refresh once so the new service worker activates.

## Android (Chrome / Edge)
1. Open CRM URL.
2. Top bar **Install** (phone) icon, or browser menu → **Install app**.
3. Confirm. Open from home screen / app drawer.

## iPhone / iPad (Safari)
1. Open CRM in **Safari**.
2. Share → **Add to Home Screen** → Add.

## Desktop (Chrome / Edge)
1. Open CRM → address bar install icon or top-bar Install button.

## Shortcuts
- Live Dashboard · Inquiries · Implementation

## Troubleshooting
- Install missing → need HTTPS; clear site data; ensure not already installed.
- Old icon → uninstall PWA, clear data, reinstall.
