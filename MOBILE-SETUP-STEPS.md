# ProfitNx CRM — Mobile પર કેવી રીતે ચલાવવું

## Option A — Android APK (WebView app)

1. Server પર આ updated folder deploy કરો (IIS / Kestrel / Linux).
2. `appsettings` / Google Sheets credentials verify કરો જેથી CRM web URL live હોય (e.g. `https://your-crm.example.com`).
3. Android project: `ProfitNx-CRM-Android/`
   - `MainActivity.java` / assets માં base URL set કરો જો hardcoded હોય.
   - Android Studio થી open કરો → Build → Generate Signed APK / Bundle.
4. APK phone પર install કરો. App icon હવે official Profit Nx logo સાથે આવશે.
5. First open પર notification permission allow કરો (Pending Inquiry alerts માટે).

## Option B — Mobile browser / PWA (સૌથી સરળ)

1. Phone browser (Chrome) માં CRM URL ખોલો: `https://your-crm.example.com`
2. Login કરો.
3. Chrome menu → **Add to Home screen** / **Install app**.
4. Home screen પર ProfitNx icon આવશે (official logo).
5. Notifications allow કરો જ્યારે browser પૂછે.

## Option C — Local network test (dev)

1. PC પર `dotnet run` (project folder માં).
2. Same Wi‑Fi પર phone → `http://PC-IP:PORT` ખોલો.
3. Login → test Quotation multi-upload અને Attention popup.

## Deploy પછી

- Browser / PWA cache clear: Ctrl+F5 અથવા app uninstall+reinstall.
- Service worker update માટે offline.html / service-worker એક વાર reload.
