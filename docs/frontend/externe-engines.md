# Externe Engines: zwei Quellen, eine Liste (0.537.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


`GET /api/engine/external` liefert Engines, die DIREKT bei RookHub angemeldet sind (eigener Broker, Kennung `rhe_…`,
`source: 'rookhub'`, `online` = der Provider pollt gerade), und die des Lichess-Kontos (`eei_…`, `source: 'lichess'`,
`online: null` — Lichess sagt es nicht). Die Regeln stehen EINMAL in `features/analysis/external-engine.service.ts`:
`engineSourceOf` (Feld, sonst aus der Kennung — ältere Server schicken kein `source`), `isEngineOffline` (nur direkte
Engines können „offline“ sein) und `engineTagKey` (Zusatz hinter dem Namen in den Auswahlen: „offline“ bzw. „über
Lichess“; ohne ihn wären gleichnamige Engines beider Quellen nicht zu unterscheiden). Analysebrett, Vergleich und
Auftragsseite (`analysis-jobs.component.ts`) benutzen diese drei — keine eigene Präfix-Prüfung.

- **Profil-Engine-Karte** (`features/profile/engine-card.component.ts`): zwei Abschnitte. „Direkt mit RookHub“ zeigt
  die angemeldeten Engines mit Online-Punkt und Entfernen (`DELETE /api/external-engine/{id}`, mit Rückfrage) und
  verweist auf die API-Tokens derselben Seite; „Über Lichess (für Cloud-Engines)“ ist der bisherige Token-Block. Die
  Liste wird IMMER geladen, auch ohne Lichess-Token — sonst sähe man direkt angemeldete Engines nie. Hintergrund-
  Auswahl und Haus-Engine gelten für beide Quellen; `lichessUnreachable` blendet einen Hinweis ein, die direkten
  Engines bleiben benutzbar.
- **API-Tokens** (`features/profile/api-tokens.component.ts`): der Anlege-Dialog fragt den BEREICH (`extension` =
  Browser-Erweiterung, Vorgabe; `engine` = Engine-Provider), die Liste zeigt ihn als Spalte. Ein Engine-Token erreicht
  serverseitig NUR `/api/external-engine/*`.
