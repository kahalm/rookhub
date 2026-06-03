# RookHub als Android-App (TWA für Google Play)

RookHub ist eine installierbare PWA. Für Google Play wird sie als **TWA**
(Trusted Web Activity, via [Bubblewrap](https://github.com/GoogleChromeLabs/bubblewrap))
verpackt — eine schlanke Android-App, die `https://rookhub.oberschmid.homes`
ohne Browser-Leiste full-screen lädt.

Quelle der Wahrheit ist **`twa-manifest.json`**. Das generierte Gradle-Projekt wird
**nicht** committet (siehe `.gitignore`) — `bubblewrap update` erzeugt es aus dem Manifest.

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
bubblewrap update            # generiert das Android-Projekt aus twa-manifest.json
bubblewrap build             # erzeugt app-release-bundle.aab (signiert) + app-release-signed.apk
```
Bubblewrap lädt JDK + Android-SDK bei Bedarf selbst nach (`~/.bubblewrap`).

## CI-Build
`.github/workflows/android-twa.yml` (manuell via *Run workflow*) baut den AAB und lädt
ihn als Artefakt hoch. Benötigte **Repository-Secrets**:
- `ANDROID_KEYSTORE_BASE64` — `base64 -w0 android.keystore`
- `ANDROID_KEYSTORE_PASSWORD`
- `ANDROID_KEY_PASSWORD`

> Hinweis: Der Workflow ist hier nicht ausführbar getestet (kein Android-SDK in der
> Build-Umgebung des Repos-Autors). Bei Bedarf an die Bubblewrap-Version anpassen.

## Alternative ohne lokales Tooling
[PWABuilder.com](https://www.pwabuilder.com/) → URL eingeben → Android-Paket (AAB) +
`assetlinks.json` generieren lassen.

## Play-Veröffentlichung (Kurz)
1. AAB in der Play Console hochladen (interner Test).
2. Store-Eintrag: Beschreibung, Icon 512, Feature-Graphic 1024×500, Screenshots.
3. **Datenschutz-URL**: `https://rookhub.oberschmid.homes/privacy`.
4. Data-Safety-Formular ausfüllen (siehe Datenschutzerklärung).
5. Neue Privat-Accounts: 12 Tester / 14 Tage Closed-Test vor Production.
