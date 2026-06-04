# RookHub als Android-App (TWA für Google Play)

RookHub ist eine installierbare PWA. Für Google Play wird sie als **TWA**
(Trusted Web Activity, via [Bubblewrap](https://github.com/GoogleChromeLabs/bubblewrap))
verpackt — eine schlanke Android-App, die `https://rookhub.oberschmid.homes`
ohne Browser-Leiste full-screen lädt.

Quelle der Wahrheit sind die Manifest-Dateien:
- **`twa-manifest.json`** — Produktion (`rookhub.oberschmid.homes`, Play-Store-App).
- **`twa-manifest.dev.json`** — Dev-Variante (`rookhub-dev.oberschmid.homes`,
  separater `packageId` `…rookhub.dev`, parallel zur Prod-App installierbar).

Das generierte Gradle-Projekt wird **nicht** committet (siehe `.gitignore`) —
`bubblewrap update` erzeugt es aus dem jeweiligen Manifest. Beide Varianten signieren
mit demselben Keystore; `src/frontend/app/public/.well-known/assetlinks.json` listet
beide Package-Ids mit demselben SHA-256-Fingerprint, sodass die Datei sowohl von
Prod- als auch Dev-Host serviert beide Apps freigibt.

## Voraussetzungen (einmalig)
1. **Upload-Keystore erstellen** (geheim halten, NICHT committen):
   ```bash
   keytool -genkeypair -v -keystore android.keystore -alias rookhub \
     -keyalg RSA -keysize 2048 -validity 10000
   ```
2. **Play App Signing** beim ersten Upload aktivieren (Google verwaltet den finalen
   Signaturschlüssel; du lädst mit dem Upload-Key hoch).
3. **Digital Asset Links schließen:** den **SHA-256-Fingerprint** (aus der Play Console
   unter *Release → Setup → App signing*, bzw. `keytool -list -v -keystore android.keystore`)
   in `../src/frontend/app/public/.well-known/assetlinks.json` eintragen und ausrollen.

## Lokaler Build
```bash
npm install -g @bubblewrap/cli
cd twa

# Prod-Build (Standard — twa-manifest.json):
bubblewrap update
bubblewrap build              # → app-release-bundle.aab + app-release-signed.apk

# Dev-Build (rookhub-dev.oberschmid.homes):
cp twa-manifest.dev.json twa-manifest.json
bubblewrap update
bubblewrap build
# Danach ggf. twa-manifest.json via `git checkout` wiederherstellen,
# damit der Prod-Manifest committed/lesbar bleibt.
```
Bubblewrap lädt JDK + Android-SDK bei Bedarf selbst nach (`~/.bubblewrap`).

## CI-Build
`.github/workflows/android-twa.yml` ist via *Run workflow* manuell auslösbar und
fragt eine **Variant**-Auswahl ab:
- `prod` → `twa-manifest.json` (Default; Play-Store-App).
- `dev` → `twa-manifest.dev.json` (Dev-Backend, separate Package-Id, parallel
  zur Prod-App auf demselben Telefon installierbar).

Artefakt im Job: `rookhub-android-prod` bzw. `rookhub-android-dev`
(enthält `app-release-bundle.aab` + `app-release-signed.apk`).

Benötigte **Repository-Secrets** (für beide Varianten gleich, da derselbe Keystore):
- `ANDROID_KEYSTORE_BASE64` — `base64 -w0 android.keystore`
- `ANDROID_KEYSTORE_PASSWORD`
- `ANDROID_KEY_PASSWORD`

## Alternative ohne lokales Tooling
[PWABuilder.com](https://www.pwabuilder.com/) → URL eingeben → Android-Paket (AAB) +
`assetlinks.json` generieren lassen.

## Play-Veröffentlichung (Kurz)
1. AAB in der Play Console hochladen (interner Test).
2. Store-Eintrag: Beschreibung, Icon 512, Feature-Graphic 1024×500, Screenshots.
3. **Datenschutz-URL**: `https://rookhub.oberschmid.homes/privacy`.
4. Data-Safety-Formular ausfüllen (siehe Datenschutzerklärung).
5. Neue Privat-Accounts: 12 Tester / 14 Tage Closed-Test vor Production.
