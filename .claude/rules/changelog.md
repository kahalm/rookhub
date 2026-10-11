---
paths:
  - "**/environments/changelog.ts"
  - "**/environments/changelog-data.ts"
---

# Changelog und Version

- Im Prosatext NUR typografische Anführungszeichen `„…“` — ein gerades `"` beendet den JS-String, `ng build` (CI) wird rot,
  `ng test` merkt es nicht. Nach jedem Edit hier: `cd src/frontend/app && npx ng build app`.
- `APP_VERSION` erst unmittelbar vor dem Commit vergeben, nach frischem `git fetch` — die andere Kopie verbraucht
  sonst dieselbe Nummer.
- Rebase-Konflikte hier nach INHALT auflösen (beide Einträge behalten, eigene Version über die fremde heben),
  nie blind „ours“/„theirs“.
- `changelog-data.ts` ist ~6 000 Zeilen Verlauf — nur den Kopf lesen (`head -60`), nie die ganze Datei.
