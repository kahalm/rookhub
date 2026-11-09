# Neue Fassung erkennen: `AppUpdateService` + „Seite nicht gefunden“

`core/app-update.service.ts` ist der geteilte Umgang mit dem Service Worker (RookHub, Turnierseite, KidHub):
Hinweis „Neue Version verfügbar“ mit „Neu laden“, Telemetrie bei gescheiterter Installation, Selbstheilung bei
`UNRECOVERABLE_STATE` und die aktive Prüfung beim Start, alle 15 Minuten (`CheckIntervalMs`) und bei Rückkehr des Tabs.
`SwUpdate` wird OPTIONAL injiziert — LeagueHub und ClubHub registrieren keinen Service Worker, nutzen aber die geteilte
Nicht-gefunden-Seite.

## Nicht-gefunden-Seite prüft sofort (U1, Wunsch 2026-11-09)

Eine alte, im Service Worker gehaltene Fassung kennt neue Routen nicht (Beispiel `/games/:id/summary` aus 0.913.0) und
landet auf „Seite nicht gefunden“, obwohl es die Seite am Server gibt. `shared/not-found/not-found.component.ts`
(RookHub `'**'`, Kurz-URL-Auflöser, LeagueHub `'**'`) ruft deshalb beim Anzeigen `AppUpdateService.checkNow()`:

1. `SwUpdate.checkForUpdate()`, wenn ein Service Worker läuft;
2. immer zusätzlich `fetchServerVersion()` (`core/app-version-check.ts`): `GET /assets/changelog.json?ngsw-bypass=1`
   mit `cache: 'no-store'` (das von `scripts/changelog-json.mjs` erzeugte Asset, neuester Eintrag zuerst; `ngsw-bypass`
   weil der SW es in seiner lazy-Gruppe `changelog` hält), oberste Version gegen `APP_VERSION` per `isNewerVersion`
   (dreiteilig numerisch). Zeitlimit 5 s; jeder Fehler (offline, 404, SPA-Rückfall statt JSON) zählt als „nichts“.

Anzeige: während der Prüfung „Prüfe auf neue Version …“; neuere Fassung → „Neue Version verfügbar (x.y.z). Diese Seite
gibt es vielleicht erst dort.“ (ohne bekannte Nummer, wenn nur der SW eine meldet: ohne Klammer) mit dem Hauptknopf
„Aktualisieren“ (`activateAndReload`: `activateUpdate()` und `location.replace(<dieselbe Adresse>)`, ohne SW ein harter
Reload); sonst und bei Fehlern die normale Meldung.

Grenzen: `changelog.json` wird nur ins Projekt `app` kopiert (angular.json) — in LeagueHub (ohne SW) findet die Prüfung
darum nie etwas und zeigt die normale Seite. Turnierseite, KidHub und ClubHub haben keine Nicht-gefunden-Seite (`'**'`
leitet auf die Startseite um).
