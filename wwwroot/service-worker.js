/* ProfitNx CRM — PWA shell service worker */
const CACHE_VERSION = 'profitnx-crm-shell-2026-09-10-push-v9';
const APP_SHELL = [
  '/offline.html',
  '/manifest.webmanifest',
  '/css/site.css',
  '/css/professional-ui.css',
  '/css/premium-2026.css',
  '/css/crm-reference-ui.css',
  '/css/corporate-ui-2026.css',
  '/css/crm-polish-2026-07-29.css',
  '/css/new-ui-reference-2026-07-29.css',
  '/css/final-ui-correction-2026-07-30.css',
  '/css/app-theme-switcher.css',
  '/css/live-dashboard.css',
  '/css/login-report.css',
  '/css/enterprise-ui-2026-09-08.css',
  '/js/crm-help-i18n.js',
  '/js/crm-ui.js',
  '/js/enterprise-ui-2026-09-08.js',
  '/img-profit-logo.png',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/icons/maskable-512.png',
  '/icons/favicon-32.png'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_VERSION).then(async (cache) => {
      await Promise.all(
        APP_SHELL.map((url) =>
          cache.add(url).catch(() => {})
        )
      );
    }).then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE_VERSION).map((key) => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') return;

  let url;
  try { url = new URL(request.url); } catch { return; }
  if (url.origin !== self.location.origin) return;

  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request)
        .then((response) => response)
        .catch(() => caches.match('/offline.html'))
    );
    return;
  }

  const isStaticAsset =
    ['style', 'script', 'image', 'font'].includes(request.destination) ||
    url.pathname.startsWith('/icons/') ||
    url.pathname === '/manifest.webmanifest' ||
    url.pathname.startsWith('/css/') ||
    url.pathname.startsWith('/js/');

  if (!isStaticAsset) return;

  event.respondWith(
    caches.match(request).then((cached) => {
      const fresh = fetch(request)
        .then((response) => {
          if (response && response.ok) {
            const copy = response.clone();
            caches.open(CACHE_VERSION).then((cache) => cache.put(request, copy));
          }
          return response;
        })
        .catch(() => cached);
      return cached || fresh;
    })
  );
});

self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
  // Desktop notification when CRM tab/window is minimized or backgrounded
  if (event.data && event.data.type === 'SHOW_NOTIFICATION') {
    const { title, body, icon, tag, url } = event.data;
    event.waitUntil(
      self.registration.showNotification(title || 'ProfitNx CRM', {
        body: body || '',
        icon: icon || '/icons/icon-192.png',
        badge: '/icons/icon-192.png',
        tag: tag || ('profitnx-attention-' + Date.now()),
        renotify: true,
        requireInteraction: true,
        silent: false,
        data: { url: url || '/' }
      }).catch(function () { /* ignore */ })
    );
  }
});

// Web Push payloads from the CRM server (works even when the tab is closed).
self.addEventListener('push', (event) => {
  let payload = { title: 'ProfitNx CRM', body: '', url: '/', tag: 'profitnx-push' };
  try {
    if (event.data) {
      const text = event.data.text();
      try {
        payload = Object.assign(payload, JSON.parse(text));
      } catch {
        payload.body = text;
      }
    }
  } catch (_) { /* ignore */ }

  event.waitUntil(
    self.registration.showNotification(payload.title || 'ProfitNx CRM', {
      body: payload.body || '',
      icon: payload.icon || '/icons/icon-192.png',
      badge: '/icons/icon-192.png',
      tag: payload.tag || 'profitnx-push',
      renotify: true,
      requireInteraction: true,
      data: { url: payload.url || '/' }
    })
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const targetUrl = (event.notification.data && event.notification.data.url) || '/';
  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        if (client.url.includes(self.location.origin) && 'focus' in client) {
          client.navigate(targetUrl);
          return client.focus();
        }
      }
      if (clients.openWindow) {
        return clients.openWindow(targetUrl);
      }
    })
  );
});
