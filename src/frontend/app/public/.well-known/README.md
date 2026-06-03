# Digital Asset Links (`assetlinks.json`)

Verknüpft die Android-**TWA** (Trusted Web Activity, Google Play) mit der Domain
`rookhub.oberschmid.homes` und entfernt die Browser-URL-Leiste in der App.

- Wird unter `https://rookhub.oberschmid.homes/.well-known/assetlinks.json` ausgeliefert
  (Angular kopiert `public/.well-known/**` ins Web-Root; nginx setzt `application/json`).
- `package_name`: `homes.oberschmid.rookhub` (= Android `applicationId`, siehe TWA-Projekt).

## VOR dem Release ausfüllen
`sha256_cert_fingerprints` muss den **SHA-256 des App-Signaturschlüssels** enthalten.
Bei **Play App Signing** (empfohlen) steht der Wert in der Play Console unter
*Release → Setup → App signing* (Feld „SHA-256 certificate fingerprint“). Format:
`AA:BB:CC:…` (durch Doppelpunkte getrennt). Den Platzhalter
`REPLACE_WITH_PLAY_APP_SIGNING_SHA256` durch diesen Wert ersetzen.

Mehrere Fingerprints (z. B. Upload-Key + Play-App-Signing-Key) können als
Array-Einträge ergänzt werden.
