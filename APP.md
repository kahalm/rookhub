# RookHub – offene Aufgaben

Stand: 2026-06-03. (Persönliche TODO-Liste, nicht zwingend committed.)

## Google Play / TWA — Code fertig, in 5 gestackten Branches gepusht (nicht gemergt)

In Reihenfolge mergen, **oder** einfach Branch 5 mergen (enthält 1–4):

1. `feature/play-1-icons-manifest` (0.78.1) — App-Icons + Manifest
2. `feature/play-2-account-deletion` (0.78.2) — Konto-Löschung (DSGVO)
3. `feature/play-3-privacy-impressum` (0.78.3) — Datenschutz + Impressum
4. `feature/play-4-assetlinks` (0.78.4) — Digital Asset Links + nginx
5. `feature/play-5-twa-android` (0.78.5) — TWA-Config + CI-AAB-Workflow

### Nur von dir zu erledigen
- [ ] Branches reviewen + mergen (Push baut `:dev`; `:latest`/Prod ist tag-gated)
- [ ] **Impressum**: echte Betreiberdaten (Name / Anschrift / UID) in `src/frontend/app/public/i18n/de.json` + `en.json` unter `legal.impressum.*` eintragen (Platzhalter ersetzen)
- [ ] **Google-Play-Developer-Account** prüfen/anlegen (25 $; neue Privat-Accounts: 12 Tester / 14 Tage Closed-Test vor Production)
- [ ] **Upload-Keystore** erzeugen (`keytool -genkeypair … -alias rookhub`) + **Play App Signing** aktivieren
- [ ] CI-Secrets setzen: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_PASSWORD`
- [ ] **AAB bauen**: GitHub-Action „Build Android TWA" (manuell) **oder** lokal `bubblewrap build` **oder** PWABuilder.com
- [ ] **SHA-256-Fingerprint** (aus Play App Signing) in `src/frontend/app/public/.well-known/assetlinks.json` eintragen (Platzhalter `REPLACE_WITH_PLAY_APP_SIGNING_SHA256`) + ausrollen
- [ ] Play-Listing: Beschreibung, Icon 512, Feature-Graphic 1024×500, ≥2 Screenshots
- [ ] **Datenschutz-URL** in der Play Console: `https://rookhub.oberschmid.homes/privacy`
- [ ] **Data-Safety-Formular** ausfüllen (gemäß Datenschutzerklärung)
- [x] Voraussetzung: `rookhub.oberschmid.homes` öffentlich + gültiges TLS (bestätigt)

Details/Build-Anleitung: `twa/README.md`.

## Offener Bug (geparkt — auf „weiter" warten)
- [ ] **Analyse-Engine hängt sofort bei „Berechne…/calculate"** beim Wechsel Puzzle → Analyse (nach Versions-Update). Bekannte „Berechne…"-Klasse; Verdacht: Re-Entrancy/Navigations-Race beim schnellen Moduswechsel oder SW-Update liefert kurz inkonsistente Assets. Relevante Dateien: `features/analysis/*`, `AnalysisEngineService`, `StockfishService` (Pfade verifizieren). Noch nicht untersucht.
